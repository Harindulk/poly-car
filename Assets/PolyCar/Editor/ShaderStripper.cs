using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace PolyCar.Editor
{
    /// <summary>
    /// Drops shader variants for URP features Poly Car never enables (Forward+, light layers, decals buffers,
    /// probe volumes, cookies, extra-light shadows, SSAO, probe blending, realtime GI, debug display).
    /// Keeps build times short and the player small; lightmaps and fog stay (Green Forest is baked).
    /// </summary>
    public class ShaderStripper : IPreprocessShaders
    {
        public int callbackOrder => 100;

        private static readonly HashSet<string> Unused = new HashSet<string>
        {
            "_FORWARD_PLUS", "_CLUSTER_LIGHT_LOOP", "PROBE_VOLUMES_L1", "PROBE_VOLUMES_L2",
            "_DBUFFER_MRT1", "_DBUFFER_MRT2", "_DBUFFER_MRT3", "_DECAL_LAYERS", "_LIGHT_LAYERS", "_WRITE_RENDERING_LAYERS",
            "_LIGHT_COOKIES", "_ADDITIONAL_LIGHT_SHADOWS", "_SCREEN_SPACE_OCCLUSION",
            "_REFLECTION_PROBE_BLENDING", "_REFLECTION_PROBE_BOX_PROJECTION", "_REFLECTION_PROBE_ATLAS",
            "DYNAMICLIGHTMAP_ON", "DEBUG_DISPLAY", "_ADDITIONAL_LIGHTS", "_ADDITIONAL_LIGHTS_VERTEX",
            "_SCREEN_SPACE_IRRADIANCE", "_MIXED_LIGHTING_SUBTRACTIVE",
        };

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            for (int i = data.Count - 1; i >= 0; i--)
            {
                foreach (var kw in data[i].shaderKeywordSet.GetShaderKeywords())
                {
                    if (Unused.Contains(kw.name))
                    {
                        data.RemoveAt(i);
                        break;
                    }
                }
            }
        }
    }
}
