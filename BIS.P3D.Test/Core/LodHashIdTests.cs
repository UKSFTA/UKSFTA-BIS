using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using BIS.P3D;

namespace BIS.P3D.Test.Core
{
    public class LodHashIdTests
    {
        [Fact]
        public void Compute_ShouldGenerateConsistentHash()
        {
            var vectors = new List<Vector3>
            {
                new Vector3(0, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(0, 1, 0)
            };

            var hash1 = LodHashId.Compute(vectors);
            var hash2 = LodHashId.Compute(vectors);

            Assert.Equal(hash1.Hash15AsString, hash2.Hash15AsString);
            Assert.Equal(hash1.Hash8AsString, hash2.Hash8AsString);
            Assert.Equal(hash1.Vertex, hash2.Vertex);
        }

        [Fact]
        public void Compute_EmptyVectors_ShouldReturnEmptyHash()
        {
            var hash = LodHashId.Compute(new List<Vector3>());
            Assert.Same(LodHashId.Empty, hash);
        }

        [Fact]
        public void Compute_SingleVertex_ReturnsNonEmpty()
        {
            var hash = LodHashId.Compute(new[] { new Vector3(1.5f, 2.5f, 3.5f) });
            Assert.NotSame(LodHashId.Empty, hash);
            Assert.Equal(1, hash.Vertex);
            Assert.NotNull(hash.Hash15);
            Assert.NotNull(hash.Hash8);
        }

        [Fact]
        public void Compute_TwoIdenticalVerts_ReturnsOneVertex()
        {
            var hash = LodHashId.Compute(new[]
            {
                new Vector3(10, 20, 30),
                new Vector3(10, 20, 30)
            });
            Assert.Equal(1, hash.Vertex);
        }

        [Fact]
        public void Compute_TwoDifferentVerts_ReturnsTwoVertices()
        {
            var hash = LodHashId.Compute(new[]
            {
                new Vector3(0, 0, 0),
                new Vector3(100, 200, 300)
            });
            Assert.Equal(2, hash.Vertex);
        }

        [Fact]
        public void Compute_SameVerts_DifferentOrder_SameHash()
        {
            var a = new List<Vector3>
            {
                new Vector3(0, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(0, 1, 0)
            };
            var b = new List<Vector3>
            {
                new Vector3(0, 1, 0),
                new Vector3(0, 0, 0),
                new Vector3(1, 0, 0)
            };

            var hashA = LodHashId.Compute(a);
            var hashB = LodHashId.Compute(b);
            Assert.Equal(hashA.Hash15AsString, hashB.Hash15AsString);
            Assert.Equal(hashA.Vertex, hashB.Vertex);
        }

        [Fact]
        public void Compute_ManyVertices_ProducesStableHash()
        {
            var vectors = new List<Vector3>();
            for (int x = 0; x < 10; x++)
                for (int y = 0; y < 10; y++)
                    for (int z = 0; z < 10; z++)
                        vectors.Add(new Vector3(x * 0.5f, y * 0.5f, z * 0.5f));

            var hash1 = LodHashId.Compute(vectors);
            var hash2 = LodHashId.Compute(vectors);
            Assert.Equal(hash1.Hash15AsString, hash2.Hash15AsString);
            Assert.Equal(hash1.Vertex, hash2.Vertex);
        }

        [Fact]
        public void Compute_ManyVertices_ReducesDuplicatePositions()
        {
            var vectors = new List<Vector3>();
            for (int i = 0; i < 100; i++)
                vectors.Add(new Vector3(50, 50, 50));

            var hash = LodHashId.Compute(vectors);
            Assert.Equal(1, hash.Vertex);
        }

        [Fact]
        public void Compute_Vector3POverload_ProducesHash()
        {
            var vectors = new BIS.Core.Math.Vector3P[]
            {
                new BIS.Core.Math.Vector3P(0, 0, 0),
                new BIS.Core.Math.Vector3P(1, 0, 0),
                new BIS.Core.Math.Vector3P(0, 1, 0)
            };

            var hash = LodHashId.Compute((IEnumerable<BIS.Core.Math.Vector3P>)vectors);
            Assert.NotSame(LodHashId.Empty, hash);
            Assert.Equal(3, hash.Vertex);
        }

        [Fact]
        public void Compute_DifferentInputs_ProduceDifferentHashes()
        {
            var hashA = LodHashId.Compute(new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0) });
            var hashB = LodHashId.Compute(new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0) });
            Assert.NotEqual(hashA.Hash15AsString, hashB.Hash15AsString);
        }
    }
}
