using BIS.Core.Math;
using BIS.Core.Streams;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BIS.P3D
{
    public class P3D : IReadObject
    {
        private MLOD.MLOD editable;
        private ODOL.ODOL binarized;

        public P3D(Stream stream) : this(new BinaryReaderEx(stream)) { }

        public P3D(BinaryReaderEx input) { Read(input); }

        public IModelInfo ModelInfo =>
            binarized?.ModelInfo
            ?? editable?.ModelInfo;

        public IEnumerable<ILevelOfDetail> LODs =>
            binarized?.Lods.AsEnumerable<ILevelOfDetail>()
            ?? editable?.Lods?.AsEnumerable<ILevelOfDetail>();

        public bool IsEditable => editable != null;

        public int Version => binarized?.Version ?? editable?.Version ?? 0;

        public float Mass => binarized?.ModelInfo?.Mass ?? 0.0f;

        public static bool IsODOL(string filePath)
        {
            return IsODOL(File.OpenRead(filePath));
        }

        public static bool IsODOL(Stream stream)
        {
            bool result = false;
            if (stream.ReadByte() == 'O'
            && stream.ReadByte() == 'D'
            && stream.ReadByte() == 'O'
            && stream.ReadByte() == 'L')
                result = true; ;

            stream.Position = 0;

            return result;
        }
        public static bool IsMLOD(string filePath)
        {
            return IsMLOD(File.OpenRead(filePath));
        }

        public static bool IsMLOD(Stream stream)
        {
            bool result = false;
            if (stream.ReadByte() == 'M'
            && stream.ReadByte() == 'L'
            && stream.ReadByte() == 'O'
            && stream.ReadByte() == 'D')
                result = true; ;

            stream.Position = 0;

            return result;
        }

        public void Read(BinaryReaderEx input)
        {
            var signature = input.ReadAscii(4);
            switch (signature)
            {
                case "ODOL":
                    binarized = new ODOL.ODOL();
                    binarized.ReadContent(input);
                    editable = null;
                    break;
                case "MLOD":
                    editable = new MLOD.MLOD();
                    editable.ReadContent(input);
                    binarized = null;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown P3D format '{signature}'");
            }
        }

        public void Write(BinaryWriterEx output)
        {
            if (binarized != null)
            {
                binarized.Write(output);
            }
            else if (editable != null)
            {
                editable.Write(output);
            }
            else
            {
                throw new InvalidOperationException("P3D structure is not initialized.");
            }
        }

        public ODOL.ODOL ODOL => binarized;

        public MLOD.MLOD MLOD => editable;

        public bool IsODOLFormat => binarized != null;
        public bool IsMLODFormat => editable != null;
    }
}
