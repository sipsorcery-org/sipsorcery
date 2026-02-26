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

        private AudioScope _audioScope;
        private SharpGL.Shaders.ShaderProgram? _prog;
        private SharpGL.Shaders.ShaderProgram? _clearProg;
        private float[]? _clearRectangle;

        public AudioScopeOpenGL(AudioScope audioScope)
        {
            _audioScope = audioScope;
        }

        public void Initialise(OpenGL gl)
        {
            // Load the main program.

            string? fragmentShaderCode = null;
            using (StreamReader sr = new StreamReader(FRAGMENT_SHADER_PATH))
            {
                fragmentShaderCode = sr.ReadToEnd();
            }

            string? vertexShaderCode = null;
            using (StreamReader sr = new StreamReader(VERTEX_SHADER_PATH))
            {
                vertexShaderCode = sr.ReadToEnd();
            }

            _prog = new SharpGL.Shaders.ShaderProgram();
            _prog.Create(gl, vertexShaderCode, fragmentShaderCode, null);

            string? geometryShaderCode = null;
            if (File.Exists(GEOMETRY_SHADER_PATH))
            {
                using (StreamReader sr = new StreamReader(GEOMETRY_SHADER_PATH))
                {
                    geometryShaderCode = sr.ReadToEnd();
                }

                Shader geometryShader = new Shader();
                geometryShader.Create(gl, OpenGL.GL_GEOMETRY_SHADER, geometryShaderCode);
                gl.AttachShader(_prog.ShaderProgramObject, geometryShader.ShaderObject);
            }

            gl.LinkProgram(_prog.ShaderProgramObject);

            // Now that we've compiled and linked the shader, check it's link status.If it's not linked properly, we're
            //  going to throw an exception.
            if (_prog.GetLinkStatus(gl) == false)
            {
                throw new SharpGL.Shaders.ShaderCompilationException(string.Format("Failed to link shader program with ID {0}.", _prog.ShaderProgramObject), _prog.GetInfoLog(gl));
            }

            // Load clear program.

            string? clearFragShaderCode = null;
            using (StreamReader sr = new StreamReader(CLEAR_FRAGMENT_SHADER_PATH))
            {
                clearFragShaderCode = sr.ReadToEnd();
            }

            string? clearVertexShaderCode = null;
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
                throw new SharpGL.Shaders.ShaderCompilationException(string.Format("Failed to link the clear shader program with ID {0}.", _clearProg.ShaderProgramObject), _clearProg.GetInfoLog(gl));
            }

            _clearRectangle = new float[] { -1.0f, -1.0f, -1.0f, 1.0f, 1.0f, -1.0f, 1.0f, 1.0f };
        }

        public void Draw(OpenGL gl, int width, int height)
        {
            // Uncomment lne below to change the background color.
            //gl.ClearColor(1.0f, 0.0f, 1.0f, 1.0f);

            gl.Viewport(0, 0, width, height);
            gl.Ortho2D(0, width, 0, height);

            gl.Clear(OpenGL.GL_COLOR_BUFFER_BIT | OpenGL.GL_DEPTH_BUFFER_BIT);  // Clear The Screen And The Depth Buffer

            if (_prog == null)
            {
                throw new ApplicationException("The main program must be assigned for the audio scope to work properly.");
            }

            int windowID = gl.GetUniformLocation(_prog.ShaderProgramObject, "window");
            int nID = gl.GetUniformLocation(_prog.ShaderProgramObject, "n");
            int baseHueID = gl.GetUniformLocation(_prog.ShaderProgramObject, "base_hue");
            int colorizeID = gl.GetUniformLocation(_prog.ShaderProgramObject, "colorize");
            int thicknessID = gl.GetUniformLocation(_prog.ShaderProgramObject, "thickness");
            int minThicknessID = gl.GetUniformLocation(_prog.ShaderProgramObject, "min_thickness");
            int thinningID = gl.GetUniformLocation(_prog.ShaderProgramObject, "thinning");
            int decayID = gl.GetUniformLocation(_prog.ShaderProgramObject, "decay");
            int desaturationID = gl.GetUniformLocation(_prog.ShaderProgramObject, "desaturation");

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
            if (_clearProg == null)
            {
                throw new ApplicationException("The clear program must be assigned for teh audio scope to work properly.");
            }

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
                gl.UseProgram(_prog.ShaderProgramObject);

                VertexBuffer vertexBuffer = new VertexBuffer();
                vertexBuffer.Create(gl);
                vertexBuffer.Bind(gl);
                vertexBuffer.SetData(gl, 0, data, false, MAIN_DATA_STRIDE);

                gl.DrawArrays(OpenGL.GL_LINE_STRIP_ADJACENCY, 0, data.Length);
            }
        }
    }
}
