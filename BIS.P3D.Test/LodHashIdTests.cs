using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using BIS.P3D;

namespace BIS.P3D.Test
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
    }
}
