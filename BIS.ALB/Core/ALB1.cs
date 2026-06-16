using BIS.Core.Streams;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BIS.ALB
{
    public enum ALB_Datatype : byte
    {
        Character = 1,
        Integer = 5,
        Integer2 = 6,
        Integer3 = 7,
        Integer4 = 8,
        Boolean = 9,
        Float = 10,
        String = 11,
        List = 12,
        Object = 13,
        Unknown = 15,
        Unknown2 = 19,
        Double = 20,
        DoubleArray = 21
    }

    public class ALB1
    {
        private Dictionary<int, string> tags = new Dictionary<int, string>();
        private Dictionary<int, string> classes = new Dictionary<int, string>();

        private LinkedList<ALB_Entry> entries = new LinkedList<ALB_Entry>();

        public int TagsCount => tags.Count;
        public int ClassesCount => classes.Count;
        public int EntriesCount => entries.Count;

        public class ALB_Entry
        {
            public int TagID { get; }
            public ALB_Value Value { get; }
            public ALB_Datatype DataType { get; }

            public ALB_Entry(BinaryReaderEx input, int? layerVersion = null)
            {
                TagID = input.ReadInt16();
                DataType = (ALB_Datatype)input.ReadByte();
                Value = ALB_Value.ReadALBValue(DataType, input, layerVersion);
            }

            public void WriteTo(BinaryWriterEx output)
            {
                output.Write((short)TagID);
                output.Write((byte)DataType);
                Value.WriteTo(output);
            }
        }

        #region ValueTypes
        public abstract class ALB_Value
        {
            public static ALB_Value ReadALBValue(ALB_Datatype dataType, BinaryReaderEx input, int? layerVersion = null)
            {
                switch (dataType)
                {
                    case ALB_Datatype.Boolean:
                        return new ALB_SimpleValue<bool>(input.ReadBoolean());
                    case ALB_Datatype.Character:
                        return new ALB_SimpleValue<char>(input.ReadChar());
                    case ALB_Datatype.Float:
                        return new ALB_SimpleValue<float>(input.ReadSingle());
                    case ALB_Datatype.DoubleArray:
                        return new ALB_DoubleArray(input);
                    case ALB_Datatype.Integer:
                        return new ALB_SimpleValue<int>(input.ReadInt32());
                    case ALB_Datatype.Integer2: //mnPriority
                        return new ALB_SimpleValue<int>(input.ReadInt32());
                    case ALB_Datatype.Integer3: //objectCount, Hash (uint?)
                        return new ALB_SimpleValue<int>(input.ReadInt32());
                    case ALB_Datatype.Integer4:
                        return new ALB_SimpleValue<int>(input.ReadInt32());
                    case ALB_Datatype.List:
                        return new ALB_List(input, layerVersion);
                    case ALB_Datatype.Object:
                        return new ALB_Object(input);
                    case ALB_Datatype.String:
                        return new ALB_SimpleValue<string>(input.ReadAscii());
                    case ALB_Datatype.Unknown: //KeyValue?
                        return new ALB_Unknown(input);
                    case ALB_Datatype.Unknown2:
                        return new ALB_Unknown2(input);
                    case ALB_Datatype.Double:
                        return new ALB_SimpleValue<double>(input.ReadDouble());

                    default:
                        throw new FormatException();
                }
            }

            public abstract string ToString(ALB1 alb, int indLvl = 0);
            public abstract void WriteTo(BinaryWriterEx output);
        }

        public class ALB_SimpleValue<T> : ALB_Value
        {
            public T Value { get; }
            public ALB_SimpleValue(T value)
            {
                this.Value = value;
            }

            public override string ToString(ALB1 alb, int indLvl = 0)
            {
                if (Value is string) return $"\"{Value}\"";
                return Value.ToString();
            }

            public override void WriteTo(BinaryWriterEx output)
            {
                if (Value is bool bv)
                    output.Write(bv);
                else if (Value is char cv)
                    output.Write((byte)cv);
                else if (Value is float fv)
                    output.Write(fv);
                else if (Value is int iv)
                    output.Write(iv);
                else if (Value is string sv)
                {
                    var bytes = Encoding.ASCII.GetBytes(sv);
                    output.Write((ushort)bytes.Length);
                    output.Write(bytes);
                }
                else if (Value is double dv)
                    output.Write(dv);
            }
        }

        public class ALB_List : ALB_Value
        {
            int size;
            ALB_Entry[] entries;
            public ObjectTreeNode treeRoot;

            public ALB_List(BinaryReaderEx input, int? layerVersion = null)
            {
                size = input.ReadInt32();
                var nEntries = input.ReadInt32();

                if (nEntries > 0 && (size - 4 == nEntries))
                {
                    if (!layerVersion.HasValue)
                        throw new FormatException("No layerVersion specified before reading ObjectTree");
                    treeRoot = new ObjectTreeNode(input, layerVersion.Value);
                }
                else
                {
                    entries = Enumerable.Range(0, nEntries).Select(_ => new ALB_Entry(input)).ToArray();
                }
            }

            public override string ToString(ALB1 alb, int indLvl = 0)
            {
                if (entries == null || entries.Length == 0) return "Empty List";

                return $"\r\n{alb.EntriesToString(entries, indLvl + 1)}";
            }

            public override void WriteTo(BinaryWriterEx output)
            {
                if (entries != null && entries.Length > 0)
                {
                    var mem = new MemoryStream();
                    var w = new BinaryWriterEx(mem, true);
                    foreach (var e in entries)
                        e.WriteTo(w);
                    var data = mem.ToArray();
                    output.Write(4 + data.Length);
                    output.Write(entries.Length);
                    output.Write(data);
                }
                else
                {
                    output.Write(4);
                    output.Write(0);
                }
            }
        }

        public class ALB_Object : ALB_Value
        {
            int size;
            public int classID;
            int objectID;
            LinkedList<ALB_Entry> entries = new LinkedList<ALB_Entry>();

            public ALB_Object(BinaryReaderEx input)
            {
                size = input.ReadInt32();
                classID = input.ReadInt16();
                objectID = input.ReadInt32();

                var bytesRead = 6;
                while (bytesRead < size)
                {
                    var pos = input.Position;
                    entries.AddLast(new ALB_Entry(input));
                    bytesRead += (int)(input.Position - pos);
                }
            }
            public override string ToString(ALB1 alb, int indLvl = 0)
            {
                return $"\r\n{alb.EntriesToString(entries, indLvl + 1)}";
            }

            public override void WriteTo(BinaryWriterEx output)
            {
                var mem = new MemoryStream();
                var w = new BinaryWriterEx(mem, true);
                w.Write((short)classID);
                w.Write(objectID);
                foreach (var e in entries)
                    e.WriteTo(w);
                var data = mem.ToArray();
                output.Write(data.Length);
                output.Write(data);
            }
        }

        public class ALB_Unknown : ALB_Value
        {
            ALB_Entry entry1;
            ALB_Entry entry2;

            public ALB_Unknown(BinaryReaderEx input)
            {
                entry1 = new ALB_Entry(input);
                entry2 = new ALB_Entry(input);
            }

            public override string ToString(ALB1 alb, int indLvl = 0)
            {
                return $"\r\n{alb.EntryToString(entry1, indLvl + 1)}\r\n{alb.EntryToString(entry2, indLvl + 1)}";
            }

            public override void WriteTo(BinaryWriterEx output)
            {
                entry1.WriteTo(output);
                entry2.WriteTo(output);
            }
        }

        public class ALB_Unknown2 : ALB_Value
        {
            byte[] data;

            public ALB_Unknown2(BinaryReaderEx input)
            {
                data = input.ReadBytes(21);
            }

            public override string ToString(ALB1 alb, int indLvl = 0)
            {
                return string.Join(",", data);
            }

            public override void WriteTo(BinaryWriterEx output)
            {
                output.Write(data);
            }
        }

        public class ALB_DoubleArray : ALB_Value
        {
            double[] values;

            public ALB_DoubleArray(BinaryReaderEx input)
            {
                var n = input.ReadByte();
                values = Enumerable.Range(0, n).Select(_ => input.ReadDouble()).ToArray();
            }

            public override string ToString(ALB1 alb, int indLvl = 0)
            {
                return string.Join(", ", values);
            }

            public override void WriteTo(BinaryWriterEx output)
            {
                output.Write((byte)values.Length);
                foreach (var v in values)
                    output.Write(v);
            }
        }
        #endregion

        public ALB1(BinaryReaderEx input)
        {
            var sig = input.ReadAscii(4);
            if (sig != "ALB1")
                throw new FormatException("ALB1 signature missing");

            //unknown data
            input.ReadBytes(15);

            var nTags = input.ReadInt32();

            for (int i = 0; i < nTags; i++)
            {
                var tagID = input.ReadUInt16();
                var name = input.ReadAscii();

                tags[tagID] = name;
            }

            //unknown data
            input.ReadBytes(3);

            var nClasses = input.ReadInt32();

            for (int i = 0; i < nClasses; i++)
            {
                var classID = input.ReadUInt16();
                var name = input.ReadAscii();

                classes[classID] = name;
            }

            //unknown data
            input.ReadBytes(6);

            int? layerVersion = null;
            while (input.Position < input.BaseStream.Length)
            {
                var e = new ALB_Entry(input, layerVersion);
                if (tags[e.TagID].Equals("mlayerversion", StringComparison.OrdinalIgnoreCase))
                    layerVersion = (e.Value as ALB_SimpleValue<int>).Value;

                entries.AddLast(e);
            }
        }

        private string EntryToString(ALB_Entry e, int indLvl = 0)
        {
            var tag = tags[e.TagID];

            var cls = (e.Value is ALB_Object obj) ? $"({classes[obj.classID]})" : "";
            var ind = new string(' ', 4 * indLvl);
            return $"{ind}{tag}{cls}={e.Value.ToString(this, indLvl)}";
        }

        private string EntriesToString(IEnumerable<ALB_Entry> entries, int indLvl = 0)
        {
            var res = new StringBuilder();
            foreach (var e in entries)
            {
                res.AppendLine(EntryToString(e, indLvl));
            }

            return res.ToString();
        }

        public string ExtractObjectData()
        {
            var treeEntry = entries.FirstOrDefault(e => tags[e.TagID].Equals("tree"));
            var sb = new StringBuilder();
            if (treeEntry != null)
            {
                var listValue = treeEntry.Value as ALB_List;
                if (listValue.treeRoot != null)
                {
                    var objData = new LinkedList<ObjectTreeLeaf>();
                    ExtractObjectData(listValue.treeRoot, objData);

                    foreach (var objNode in objData)
                    {
                        sb.AppendLine(objNode.ToString());
                    }
                }
            }

            return sb.ToString();
        }

        public void ExtractObjectData(ObjectTreeNode node, LinkedList<ObjectTreeLeaf> list)
        {
            if (node.NodeType == 16)
            {
                for (int i = 0; i < 4; i++)
                {
                    if (node.Objects[i] != null)
                        list.AddLast(node.Objects[i]);
                }
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    if (node.Childs[i] != null)
                        ExtractObjectData(node.Childs[i], list);
                }
            }
        }

        public override string ToString()
        {
            return EntriesToString(entries);
        }

        public void SaveTo(string path)
        {
            using var stream = File.Create(path);
            var output = new BinaryWriterEx(stream);

            output.Write(Encoding.ASCII.GetBytes("ALB1"));
            output.Write(new byte[15]);

            output.Write(tags.Count);
            foreach (var kvp in tags)
            {
                output.Write((ushort)kvp.Key);
                var nameBytes = Encoding.ASCII.GetBytes(kvp.Value);
                output.Write((ushort)nameBytes.Length);
                output.Write(nameBytes);
            }

            output.Write(new byte[3]);

            output.Write(classes.Count);
            foreach (var kvp in classes)
            {
                output.Write((ushort)kvp.Key);
                var nameBytes = Encoding.ASCII.GetBytes(kvp.Value);
                output.Write((ushort)nameBytes.Length);
                output.Write(nameBytes);
            }

            output.Write(new byte[6]);

            foreach (var entry in entries)
                entry.WriteTo(output);
        }
    }
}
