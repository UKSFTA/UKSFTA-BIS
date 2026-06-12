using BIS.Core.Streams;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BIS.Core.Config
{
    public class ParamFile : IReadObject
    {
        public ParamClass Root { get; set; }
        public List<KeyValuePair<string, int>> EnumValues { get; private set; }
        public int OfpVersion { get; set; } = 55;
        public int Version { get; set; } = 21;

        public ParamFile()
        {
            EnumValues = new List<KeyValuePair<string, int>>(10);
        }

        public ParamFile(System.IO.Stream stream)
        {
            Read(new BinaryReaderEx(stream));
        }

        public void Read(BinaryReaderEx input)
        {
            var sig = new char[] { '\0', 'r', 'a', 'P' };
            if (!input.ReadBytes(4).SequenceEqual(sig.Select(c => (byte)c)))
                throw new ArgumentException();

            OfpVersion = input.ReadInt32();
            Version = input.ReadInt32();
            var offsetToEnums = input.ReadInt32();

            Root = new ParamClass(input, "rootClass");

            input.Position = offsetToEnums;
            var nEnumValues = input.ReadInt32();
            EnumValues = Enumerable.Range(0, nEnumValues).Select(_ => new KeyValuePair<string, int>(input.ReadAsciiz(), input.ReadInt32())).ToList();
        }

        public void Write(BinaryWriterEx writer)
        {
            // Phase 1: compute body file offsets via dry-run
            var bodySizes = new List<int>();
            var childRefs = new List<(int parentIdx, int childIdx)>();
            ComputeBodySizes(Root, -1, bodySizes, childRefs);

            int[] bodyOff = new int[bodySizes.Count];
            int curOff = 16;
            for (int i = 0; i < bodySizes.Count; i++)
            {
                bodyOff[i] = curOff;
                curOff += bodySizes[i];
            }

            // Phase 2: write header
            writer.Write((byte)0);
            writer.Write(new[] { (byte)'r', (byte)'a', (byte)'P' });
            writer.Write(OfpVersion);
            writer.Write(Version);

            long enumOffsetPos = writer.Position;
            writer.Write(0);

            // Phase 3: write entry lists in DFS order.
            // bodyOff[i] was computed from dry-run sizes in DFS order.
            // Writing entry lists in the same DFS order means writer.Position
            // naturally advances to bodyOff[nextIdx] — no seeking needed.
            int nextIdx = 0;
            WriteEntryList(writer, Root, -1, ref nextIdx, childRefs, bodyOff);

            // Phase 4: enum values
            int enumOffset = (int)writer.Position;
            writer.Write(EnumValues.Count);
            foreach (var kvp in EnumValues)
            {
                writer.WriteAsciiz(kvp.Key);
                writer.Write(kvp.Value);
            }

            long endPos = writer.Position;
            writer.Position = enumOffsetPos;
            writer.Write(enumOffset);
            writer.Position = endPos;
        }

        /// <summary>
        /// Writes a class's entry list (baseClass + nEntries + entries) directly to writer,
        /// then recursively writes all child entry lists in DFS order.
        /// ParamClass subclass entries are written with the correct body offset from bodyOff.
        /// The DFS order matches the dry-run order so positions align with bodyOff offsets.
        /// </summary>
        private void WriteEntryList(BinaryWriterEx writer, ParamClass cls, int parentIdx,
            ref int nextIdx, List<(int parentIdx, int childIdx)> childRefs, int[] bodyOff)
        {
            int myIdx = nextIdx++;

            writer.WriteAsciiz(cls.BaseClassName);
            writer.WriteCompactInteger(cls.Entries.Count);

            // Get children indices in the same DFS order as dry-run
            var myChildren = childRefs
                .Where(r => r.parentIdx == myIdx)
                .Select(r => r.childIdx)
                .ToList();

            int childPos = 0;
            foreach (var entry in cls.Entries)
            {
                switch (entry)
                {
                    case ParamClass childCls:
                        writer.Write((byte)EntryType.Class);
                        writer.WriteAsciiz(childCls.Name);
                        writer.Write(bodyOff[myChildren[childPos++]]);
                        break;
                    case ParamValue pv:
                        writer.Write((byte)EntryType.Value);
                        writer.Write((byte)pv.Value.Type);
                        writer.WriteAsciiz(pv.Name);
                        WriteRawValue(writer, pv.Value);
                        break;
                    case ParamArray pa:
                        writer.Write((byte)EntryType.Array);
                        writer.WriteAsciiz(pa.Name);
                        WriteRawArray(writer, pa.Array);
                        break;
                    case ParamArraySpec pas:
                        writer.Write((byte)EntryType.ArraySpec);
                        writer.Write(pas.Flag);
                        writer.WriteAsciiz(pas.Name);
                        WriteRawArray(writer, pas.Array);
                        break;
                    case ParamExternClass pec:
                        writer.Write((byte)EntryType.ClassDecl);
                        writer.WriteAsciiz(pec.Name);
                        break;
                    case ParamDeleteClass pdc:
                        writer.Write((byte)EntryType.ClassDelete);
                        writer.WriteAsciiz(pdc.Name);
                        break;
                }
            }

            // Recurse into children in the same DFS order as the entry list iteration
            childPos = 0;
            foreach (var entry in cls.Entries)
            {
                if (entry is ParamClass)
                {
                    var childCls = (ParamClass)entry;
                    WriteEntryList(writer, childCls, myIdx, ref nextIdx, childRefs, bodyOff);
                }
            }
        }

        /// <summary>
        /// Dry-run: compute byte sizes and child-parent relationships for all class bodies.
        /// bodySizes[0] = root, bodySizes[1+] = nested classes in depth-first order.
        /// </summary>
        private int ComputeBodySizes(ParamClass cls, int parentIdx, List<int> bodySizes, List<(int parent, int child)> bodyRefs)
        {
            int myIdx = bodySizes.Count;
            bodySizes.Add(0);
            if (parentIdx >= 0)
                bodyRefs.Add((parentIdx, myIdx));

            int size = cls.BaseClassName.Length + 1;
            size += CompactIntSize(cls.Entries.Count);

            foreach (var entry in cls.Entries)
            {
                switch (entry)
                {
                    case ParamClass childCls:
                        size += 1;
                        size += childCls.Name.Length + 1;
                        size += 4;
                        // Recurse to compute child's body size into bodySizes[childIdx],
                        // but do NOT add child body size to parent — each body is flat/sequential.
                        ComputeBodySizes(childCls, myIdx, bodySizes, bodyRefs);
                        break;
                    case ParamValue pv:
                        size += 2;
                        size += pv.Name.Length + 1;
                        size += RawValueSize(pv.Value);
                        break;
                    case ParamArray pa:
                        size += 1;
                        size += pa.Name.Length + 1;
                        size += RawArraySize(pa.Array);
                        break;
                    case ParamArraySpec pas:
                        size += 5;
                        size += pas.Name.Length + 1;
                        size += RawArraySize(pas.Array);
                        break;
                    case ParamExternClass pec:
                        size += 1;
                        size += pec.Name.Length + 1;
                        break;
                    case ParamDeleteClass pdc:
                        size += 1;
                        size += pdc.Name.Length + 1;
                        break;
                }
            }

            bodySizes[myIdx] = size;
            return size;
        }

        private static int CompactIntSize(int value)
        {
            int size = 0;
            do
            {
                size++;
                value >>= 7;
            } while (value != 0);
            return size;
        }

        private static int RawArraySize(RawArray array)
        {
            int size = CompactIntSize(array.Entries.Count);
            foreach (var rv in array.Entries)
            {
                size += 1; // type byte
                size += RawValueSize(rv);
            }
            return size;
        }

        private static int RawValueSize(RawValue value)
        {
            switch (value.Type)
            {
                case ValueType.Generic:
                case ValueType.Expression:
                    var s = value.Value as string ?? "";
                    return System.Text.Encoding.UTF8.GetByteCount(s) + 1; // null terminator
                case ValueType.Float:
                    return 4;
                case ValueType.Int:
                    return 4;
                case ValueType.Int64:
                    return 8;
                case ValueType.Array:
                    return RawArraySize((RawArray)value.Value);
                default:
                    return 0;
            }
        }

        private static void WriteRawArray(BinaryWriterEx writer, RawArray array)
        {
            writer.WriteCompactInteger(array.Entries.Count);
            foreach (var rv in array.Entries)
            {
                writer.Write((byte)rv.Type);
                WriteRawValue(writer, rv);
            }
        }

        private static void WriteRawValue(BinaryWriterEx writer, RawValue value)
        {
            switch (value.Type)
            {
                case ValueType.Generic:
                case ValueType.Expression:
                    var bytes = System.Text.Encoding.UTF8.GetBytes(value.Value as string ?? "");
                    writer.Write(bytes);
                    writer.Write((byte)0);
                    break;
                case ValueType.Float:
                    writer.Write((float)value.Value);
                    break;
                case ValueType.Int:
                    writer.Write((int)value.Value);
                    break;
                case ValueType.Int64:
                    writer.Write((long)value.Value);
                    break;
                case ValueType.Array:
                    WriteRawArray(writer, (RawArray)value.Value);
                    break;
            }
        }

        public override string ToString()
        {
            return Root.ToString(0, true);
        }
    }
}
