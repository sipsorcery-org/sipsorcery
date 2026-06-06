//-----------------------------------------------------------------------------
// Filename: AudioScopeOpenGL.cs
//
// Description: Initialise the OpenGL portion of the Audio Scope demo.

// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 29 Feb 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using SharpGL;
using SharpGL.Shaders;
using SharpGL.VertexBuffers;

namespace AudioScope
{
    public class AudioScopeOpenGL
    {
        private const string VERTEX_SHADER_PATH = "shaders/line/line.vert";
        private const string FRAGMENT_SHADER_PATH = "shaders/line/line.frag";
        private const string GEOMETRY_SHADER_PATH = "shaders/line/line.geom";
        private const string CLEAR_VERTEX_SHADER_PATH = "shaders/clear/clear.vert";
        private const string CLEAR_FRAGMENT_SHADER_PATH = "shaders/clear/clear.frag";

        /// <summary>
        /// Length of each vector element passed to main GL program.
        ///  - X coordinate,
        ///  - Y coordinate,
        ///  - Angle,
        ///  - Output.
        /// </summary>
        private const int MAIN_DATA_STRIDE = 4;

        /// <summary>
        /// Length of each vector element passed to main GL program.
        ///  - X coordinate,
        ///  - Y coordinate,
        /// </summary>
        private const int CLEAR_DATA_STRIDE = 2;

        private AudioScope _audioScope = new AudioScope();
        private uint _prog;
        private SharpGL.Shaders.ShaderProgram _clearProg;
        private float[] _clearRectangle;

        public AudioScopeOpenGL(AudioScope audioScope)
        {
            _audioScope = audioScope;
        }

        public void Initialise(OpenGL gl)
        {
            // Load the main program. The pipeline is vertex -> geometry -> fragment and the
            // fragment shader's inputs (relative_length, angle, position) are produced by the
            // geometry shader. All three stages must therefore be attached BEFORE the program
            // is linked. SharpGL's ShaderProgram.Create links as soon as it's called (vertex +
            // fragment only), which leaves the fragment inputs unmatched. NVIDIA's linker
            // tolerates that, but stricter linkers (e.g. Intel) reject it, so the program is
            // built and linked manually here.

            string vertexShaderCode = File.ReadAllText(VERTEX_SHADER_PATH);
            string fragmentShaderCode = File.ReadAllText(FRAGMENT_SHADER_PATH);

            Shader vertexShader = new Shader();
            vertexShader.Create(gl, OpenGL.GL_VERTEX_SHADER, vertexShaderCode);

            Shader fragmentShader = new Shader();
            fragmentShader.Create(gl, OpenGL.GL_FRAGMENT_SHADER, fragmentShaderCode);

            _prog = gl.CreateProgram();
            gl.AttachShader(_prog, vertexShader.ShaderObject);
            gl.AttachShader(_prog, fragmentShader.ShaderObject);

            if (File.Exists(GEOMETRY_SHADER_PATH))
            {
                string geometryShaderCode = File.ReadAllText(GEOMETRY_SHADER_PATH);

                Shader geometryShader = new Shader();
                geometryShader.Create(gl, OpenGL.GL_GEOMETRY_SHADER, geometryShaderCode);
                gl.AttachShader(_prog, geometryShader.ShaderObject);
            }

            gl.LinkProgram(_prog);

            // Now that we've compiled and linked the shader, check its link status. If it's not
            // linked properly we're going to throw an exception.
            int[] linkStatus = new int[1];
            gl.GetProgram(_prog, OpenGL.GL_LINK_STATUS, linkStatus);
            if (linkStatus[0] == 0)
            {
                throw new SharpGL.Shaders.ShaderCompilationException($"Failed to link shader program with ID {_prog}.", GetProgramInfoLog(gl, _prog));
            }

            // Load clear program.

            string clearFragShaderCode = null;
            using (StreamReader sr = new StreamReader(CLEAR_FRAGMENT_SHADER_PATH))
            {
                clearFragShaderCode = sr.ReadToEnd();
            }

            string clearVertexShaderCode = null;
            using (StreamReader sr = new StreamReader(CLEAR_VERTEX_SHADER_PATH))
            {
                clearVertexShaderCode = sr.ReadToEnd();
            }

            _clearProg = new SharpGL.Shaders.ShaderProgram();
            _clearProg.Create(gl, clearVertexShaderCode, clearFragShaderCode, null);

            gl.LinkProgram(_clearProg.ShaderProgramObject);

            // Now that we've compiled and linked the shader, check it's link status. If it's not linked properly, we're
            // going to throw an exception.
            if (_clearProg.GetLinkStatus(gl) == false)
            {
                throw new SharpGL.Shaders.ShaderCompilationException($"Failed to link the clear shader program with ID {_clearProg.ShaderProgramObject}.", _clearProg.GetInfoLog(gl));
            }

            _clearRectangle = new float[] { -1.0f, -1.0f, -1.0f, 1.0f, 1.0f, -1.0f, 1.0f, 1.0f };
        }

        /// <summary>
        /// Retrieves the info log for a linked shader program (used for diagnostics when a
        /// link fails).
        /// </summary>
        private static string GetProgramInfoLog(OpenGL gl, uint program)
        {
            int[] infoLength = new int[1];
            gl.GetProgram(program, OpenGL.GL_INFO_LOG_LENGTH, infoLength);
            StringBuilder infoLog = new StringBuilder(infoLength[0]);
            gl.GetProgramInfoLog(program, infoLength[0], IntPtr.Zero, infoLog);
            return infoLog.ToString();
        }

        public void Draw(OpenGL gl, int width, int height)
        {
            // Uncomment lne below to change the background color.
            //gl.ClearColor(1.0f, 0.0f, 1.0f, 1.0f);

            gl.Viewport(0, 0, width, height);
            gl.Ortho2D(0, width, 0, height);

            gl.Clear(OpenGL.GL_COLOR_BUFFER_BIT | OpenGL.GL_DEPTH_BUFFER_BIT);  // Clear The Screen And The Depth Buffer

            int windowID = gl.GetUniformLocation(_prog, "window");
            int nID = gl.GetUniformLocation(_prog, "n");
            int baseHueID = gl.GetUniformLocation(_prog, "base_hue");
            int colorizeID = gl.GetUniformLocation(_prog, "colorize");
            int thicknessID = gl.GetUniformLocation(_prog, "thickness");
            int minThicknessID = gl.GetUniformLocation(_prog, "min_thickness");
            int thinningID = gl.GetUniformLocation(_prog, "thinning");
            int decayID = gl.GetUniformLocation(_prog, "decay");
            int desaturationID = gl.GetUniformLocation(_prog, "desaturation");

            gl.Uniform2(windowID, (float)width, (float)height);
            gl.Uniform1(nID, 5);
            gl.Uniform1(thicknessID, 5.0f);
            gl.Uniform1(minThicknessID, 1.5f);
            gl.Uniform1(thinningID, 0.05f);
            gl.Uniform1(baseHueID, 0.0f);
            gl.Uniform1(colorizeID, 1);
            gl.Uniform1(decayID, 0.3f);
            gl.Uniform1(desaturationID, 0.1f);

            // Run the clear program.
            gl.UseProgram(_clearProg.ShaderProgramObject);

            VertexBuffer clearVertexBuffer = new VertexBuffer();
            clearVertexBuffer.Create(gl);
            clearVertexBuffer.Bind(gl);
            clearVertexBuffer.SetData(gl, 0, _clearRectangle, false, CLEAR_DATA_STRIDE);

            // Attempt to get an available audio sample.
            var data = _audioScope.GetSample();

            if (data != null)
            {
                // Run the main program.
                gl.UseProgram(_prog);

                VertexBuffer vertexBuffer = new VertexBuffer();
                vertexBuffer.Create(gl);
                vertexBuffer.Bind(gl);
                vertexBuffer.SetData(gl, 0, data, false, MAIN_DATA_STRIDE);

                gl.DrawArrays(OpenGL.GL_LINE_STRIP_ADJACENCY, 0, data.Length);
            }
        }
    }
}
