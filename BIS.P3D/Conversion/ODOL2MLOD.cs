using System;
using System.Collections.Generic;
using System.Linq;
using BIS.Core.Math;
using BIS.P3D.ODOL;
using BIS.P3D.MLOD;

namespace BIS.P3D.Conversion
{
    public static class ODOL2MLOD
    {
        public static BIS.P3D.MLOD.MLOD Convert(BIS.P3D.ODOL.ODOL odol)
        {
            var lods = new List<BIS.P3D.MLOD.P3DM_LOD>();

            foreach (var lod in odol.Lods)
            {
                try
                {
                    lods.Add(ConvertLod(lod));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($" [Debug] Failed converting LOD: {ex.Message}");
                    throw;
                }
            }

            return new BIS.P3D.MLOD.MLOD(lods.ToArray());
        }

        private static BIS.P3D.MLOD.P3DM_LOD ConvertLod(BIS.P3D.ODOL.LOD odolLod)
        {
            var vertices = odolLod.Vertices;

            // Map Normals (handle both normal and compressed)
            Vector3P[] normals;
            if (odolLod.Normals != null && odolLod.Normals.Count > 0)
            {
                normals = odolLod.Normals.Select(n => n).ToArray();
            }
            else if (odolLod.NormalsCompressed != null && odolLod.NormalsCompressed.Count > 0)
            {
                normals = odolLod.NormalsCompressed.Select(n => (Vector3P)n).ToArray();
            }
            else
            {
                normals = new Vector3P[0];
            }

            var points = new Point[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                points[i] = new Point(vertices[i], PointFlags.NONE);
            }

            var faces = ConvertFaces(odolLod);

            var taggs = new List<Tagg>();

            // Map UV Sets
            if (odolLod.UvSets != null)
            {
                for (int i = 0; i < odolLod.UvSets.Length; i++)
                {
                    var uvs = odolLod.UvSets[i].GetUV();
                    var faceUVs = new float[faces.Length][,];
                    for (int f = 0; f < faces.Length; f++)
                    {
                        faceUVs[f] = new float[faces[f].VertexCount, 2];
                        for (int v = 0; v < faces[f].VertexCount; v++)
                        {
                            var vIdx = faces[f].Vertices[v].PointIndex;
                            if (uvs != null && vIdx < uvs.Length)
                            {
                                faceUVs[f][v, 0] = uvs[vIdx].X;
                                faceUVs[f][v, 1] = uvs[vIdx].Y;
                            }
                        }
                    }
                    taggs.Add(new UVSetTagg((uint)(faceUVs.Sum(f => f.Length * 4) + 4), i, faceUVs));
                }
            }

            // Map Named Selections
            if (odolLod.NamedSelections != null)
            {
                foreach (var selection in odolLod.NamedSelections)
                {
                    var pointsSelection = new byte[points.Length];
                    var facesSelection = new byte[faces.Length];
                    taggs.Add(new NamedSelectionTagg(selection.Name, pointsSelection, facesSelection));
                }
            }

            // Map Properties
            if (odolLod.NamedProperties != null)
            {
                foreach (var prop in odolLod.NamedProperties)
                {
                    taggs.Add(new PropertyTagg(prop.Item1, prop.Item2));
                }
            }

            // Must add EndOfFile tagg for MLOD validity
            taggs.Add(new EOFTagg());

            return new BIS.P3D.MLOD.P3DM_LOD(odolLod.Resolution, points, normals, faces, taggs);
        }

        private static BIS.P3D.MLOD.Face[] ConvertFaces(BIS.P3D.ODOL.LOD odolLod)
        {
            var mlodFaces = new List<BIS.P3D.MLOD.Face>();

            if (odolLod.Sections == null) return mlodFaces.ToArray();
            if (odolLod.Polygons == null)
            {
                return mlodFaces.ToArray();
            }

            foreach (var section in odolLod.Sections)
            {
                var uvs = (odolLod.UvSets != null && odolLod.UvSets.Length > 0) ? odolLod.UvSets[0].GetUV() : null;
                var facesInSection = section.GetFaces(odolLod.Polygons.Faces);

                if (facesInSection == null)
                {
                    continue;
                }

                foreach (var odolFace in facesInSection)
                {
                    var vertexCount = odolFace.VertexIndices.Length;

                    var mlodVertices = new BIS.P3D.MLOD.Vertex[4]; // MLOD faces seem to expect 4 vertices in the structure
                    for (int i = 0; i < 4; i++)
                    {
                        if (i < vertexCount)
                        {
                            var vIdx = odolFace.VertexIndices[vertexCount - 1 - i];
                            int normalIdx = vIdx;
                            if (odolLod.Normals != null && normalIdx >= odolLod.Normals.Count)
                            {
                                normalIdx = 0;
                            }
                            mlodVertices[i] = new BIS.P3D.MLOD.Vertex(
                                vIdx,
                                normalIdx,
                                (uvs != null && vIdx < uvs.Length) ? uvs[vIdx].X : 0,
                                (uvs != null && vIdx < uvs.Length) ? uvs[vIdx].Y : 0
                            );
                        }
                        else
                        {
                            mlodVertices[i] = new BIS.P3D.MLOD.Vertex(0, 0, 0, 0);
                        }
                    }

                    string texture = (odolLod.Textures != null && section.TextureIndex >= 0 && section.TextureIndex < odolLod.Textures.Length) ? odolLod.Textures[section.TextureIndex] : "";
                    string material = (odolLod.Materials != null && section.MaterialIndex >= 0 && section.MaterialIndex < odolLod.Materials.Length) ? odolLod.Materials[section.MaterialIndex].MaterialName : (section.Material ?? "");

                    mlodFaces.Add(new BIS.P3D.MLOD.Face(vertexCount, mlodVertices, BIS.P3D.MLOD.FaceFlags.DEFAULT, texture, material));
                }
            }
            return mlodFaces.ToArray();
        }
    }
}
