using System;
using System.Collections.Generic;
using System.Linq;
using BIS.Core;
using BIS.Core.Math;
using BIS.P3D.ODOL;
using BIS.P3D.MLOD;

namespace BIS.P3D.Conversion
{
    public static class ODOL2MLOD
    {
        /// <summary>ODOL version threshold for 32-bit (vs 16-bit) face indices.</summary>
        private const int LongFaceIndicesVersion = 69;

        public static BIS.P3D.MLOD.MLOD Convert(BIS.P3D.ODOL.ODOL odol)
        {
            var lods = new List<BIS.P3D.MLOD.P3DM_LOD>();

            foreach (var lod in odol.Lods)
            {
                try
                {
                    lods.Add(ConvertLod(lod, odol.Version));
                }
                catch (Exception ex)
                {
                    Terminal.Muted($"Failed converting LOD: {ex.Message}");
                    throw;
                }
            }

            return new BIS.P3D.MLOD.MLOD(lods.ToArray());
        }

        private static BIS.P3D.MLOD.P3DM_LOD ConvertLod(BIS.P3D.ODOL.LOD odolLod, int odolVersion)
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

            var (faces, odolFaceIndexMap) = ConvertFaces(odolLod, odolVersion, normals);

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

            if (odolLod.NamedSelections != null)
            {
                foreach (var selection in odolLod.NamedSelections)
                {
                    var pointsSelection = new byte[points.Length];
                    var facesSelection = new byte[faces.Length];

                    if (selection.SelectedVertices != null)
                    {
                        var vertSet = new HashSet<int>(selection.SelectedVertices);
                        for (int i = 0; i < points.Length; i++)
                            if (vertSet.Contains(i)) pointsSelection[i] = 1;
                    }

                    if (selection.SelectedFaces != null)
                    {
                        var faceSet = new HashSet<int>(selection.SelectedFaces);
                        for (int i = 0; i < odolFaceIndexMap.Length; i++)
                            if (faceSet.Contains(odolFaceIndexMap[i])) facesSelection[i] = 1;
                    }

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

        /// <summary>
        /// Converts ODOL faces to MLOD faces, triangulating n-gons (5+ vertices).
        /// Returns both the MLOD faces and a mapping from each MLOD face to its
        /// original ODOL face index (for named selection mapping).
        /// </summary>
        private static (Face[] Faces, int[] OdolFaceIndex) ConvertFaces(BIS.P3D.ODOL.LOD odolLod, int odolVersion, Vector3P[] normals)
        {
            var mlodFaces = new List<Face>();
            var odolIndexMap = new List<int>();

            if (odolLod.Sections == null) return (mlodFaces.ToArray(), odolIndexMap.ToArray());
            if (odolLod.Polygons == null) return (mlodFaces.ToArray(), odolIndexMap.ToArray());

            var allFaces = odolLod.Polygons.Faces;
            var uvs = (odolLod.UvSets != null && odolLod.UvSets.Length > 0) ? odolLod.UvSets[0].GetUV() : null;

            bool isShortFaceIndices = odolVersion < LongFaceIndicesVersion;
            uint sizeOfFace3 = isShortFaceIndices ? 8u : 16u;
            uint padOfFace4 = isShortFaceIndices ? 2u : 4u;

            foreach (var section in odolLod.Sections)
            {
                uint position = 0u;
                for (int faceIdx = 0; faceIdx < allFaces.Length && position < section.FaceUpperIndex; faceIdx++)
                {
                    if (position >= section.FaceLowerIndex && position < section.FaceUpperIndex)
                    {
                        var odolFace = allFaces[faceIdx];
                        var convFaces = ConvertOneFace(odolFace, section, odolLod, uvs, normals);
                        foreach (var mf in convFaces)
                        {
                            mlodFaces.Add(mf);
                            odolIndexMap.Add(faceIdx);
                        }
                    }
                    position += sizeOfFace3;
                    if (allFaces[faceIdx].VertexIndices.Length == 4)
                        position += padOfFace4;
                }
            }

            return (mlodFaces.ToArray(), odolIndexMap.ToArray());
        }

        private static Vertex MakeMlodVertex(int vertexIndex, Vector3P[] normals, System.Numerics.Vector2[] uvs)
        {
            int normalIdx = vertexIndex;
            if (normals != null && normalIdx >= normals.Length)
                normalIdx = 0;
            return new Vertex(
                vertexIndex,
                normalIdx,
                (uvs != null && vertexIndex < uvs.Length) ? uvs[vertexIndex].X : 0,
                (uvs != null && vertexIndex < uvs.Length) ? uvs[vertexIndex].Y : 0
            );
        }

        private static List<Face> ConvertOneFace(
            Polygon odolFace, Section section, BIS.P3D.ODOL.LOD odolLod,
            System.Numerics.Vector2[] uvs, Vector3P[] normals)
        {
            var result = new List<Face>();
            var vertexCount = odolFace.VertexIndices.Length;

            string texture = (odolLod.Textures != null && section.TextureIndex >= 0 && section.TextureIndex < odolLod.Textures.Length)
                ? odolLod.Textures[section.TextureIndex] : "";
            string material = (odolLod.Materials != null && section.MaterialIndex >= 0 && section.MaterialIndex < odolLod.Materials.Length)
                ? odolLod.Materials[section.MaterialIndex].MaterialName : (section.Material ?? "");

            if (vertexCount <= 4)
            {
                // ODOL order reversed for MLOD winding convention
                var mlodVertices = new Vertex[4];
                for (int i = 0; i < 4; i++)
                {
                    if (i < vertexCount)
                    {
                        mlodVertices[i] = MakeMlodVertex(
                            odolFace.VertexIndices[vertexCount - 1 - i], normals, uvs);
                    }
                    else
                    {
                        mlodVertices[i] = new Vertex(0, 0, 0, 0);
                    }
                }
                result.Add(new Face(vertexCount, mlodVertices, FaceFlags.DEFAULT, texture, material));
            }
            else
            {
                // Fan-triangulate n-gon. ODOL order: [v0, v1, ..., v_{N-1}].
                // MLOD reverses winding: v_{N-1} becomes the fan center.
                // Triangle k: (v_{N-1}, v_{N-2-k}, v_{N-3-k}) for k = 0..N-3
                for (int tri = 0; tri < vertexCount - 2; tri++)
                {
                    var mlodVertices = new Vertex[4];
                    mlodVertices[0] = MakeMlodVertex(
                        odolFace.VertexIndices[vertexCount - 1], normals, uvs);
                    mlodVertices[1] = MakeMlodVertex(
                        odolFace.VertexIndices[vertexCount - 2 - tri], normals, uvs);
                    mlodVertices[2] = MakeMlodVertex(
                        odolFace.VertexIndices[vertexCount - 3 - tri], normals, uvs);
                    mlodVertices[3] = new Vertex(0, 0, 0, 0);

                    result.Add(new Face(3, mlodVertices, FaceFlags.DEFAULT, texture, material));
                }
            }

            return result;
        }
    }
}
