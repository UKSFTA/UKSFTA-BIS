using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using BIS.P3D.MLOD;
using BIS.Core.Streams;
using BIS.Core.Math;

namespace BIS.P3D.Test.MLOD
{
    public class MLODTests
    {
        [Fact]
        public void MLOD_Serialization_ShouldBeConsistent()
        {
            // Setup minimal MLOD
            var lods = new P3DM_LOD[]
            {
                new P3DM_LOD(1.0f, 
                    new Point[] { new Point(new Vector3P(0,0,0), PointFlags.NONE), new Point(new Vector3P(1,0,0), PointFlags.NONE), new Point(new Vector3P(0,1,0), PointFlags.NONE) },
                    new Vector3P[] { new Vector3P(0,0,1), new Vector3P(0,0,1), new Vector3P(0,0,1) },
                    new Face[] { new Face(3, new Vertex[] { new Vertex(0,0,0,0), new Vertex(1,1,1,0), new Vertex(2,2,0,1), new Vertex(0,0,0,0) }, FaceFlags.DEFAULT, "", "") },
                    new List<Tagg> { new EOFTagg() }
                )
            };
            var mlod = new BIS.P3D.MLOD.MLOD(lods);

            // Serialize
            var ms = new MemoryStream();
            mlod.WriteToStream(ms);
            var data = ms.ToArray();

            // Verify
            Assert.NotEmpty(data);
            Assert.Equal("MLOD", System.Text.Encoding.ASCII.GetString(data.Take(4).ToArray()));
        }
    }
}
