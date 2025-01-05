using System;
using System.Windows.Forms;
using SharpGL;

namespace AudioScope
{
    public partial class FormAudioScope : Form
    {
        private AudioScope _audioScope;
        private AudioScopeOpenGL _audioScopeGL;

        //public event Action<>

        public FormAudioScope()
        {
            _audioScope = new AudioScope();
            _audioScopeGL = new AudioScopeOpenGL(_audioScope);

            InitializeComponent();
        }

        public void RequestRender()
        {
            // Must be on UI thread, so use Invoke if called from another thread
            if (this.InvokeRequired)
            {
                this.Invoke((Action)(() => openGLControl1.DoRender()));
            }
            else
            {
                openGLControl1.DoRender();
            }
        }

        private void FormAudioScope_Load(object sender, EventArgs e)
        {
            _audioScope.InitAudio(AudioSourceEnum.NAudio);
            _audioScope.Start();
        }

        private void OpenGLControl_OpenGLInitialized(object sender, EventArgs e)
        {
            OpenGL gl = this.openGLControl1.OpenGL;
            _audioScopeGL.Initialise(gl);
        }

        private void openGLControl1_OpenGLDraw(object sender, RenderEventArgs e)
        {
            OpenGL gl = this.openGLControl1.OpenGL;
            _audioScopeGL.Draw(gl, this.openGLControl1.Width, this.openGLControl1.Height);

            byte[] pixelData = new byte[this.openGLControl1.Width * this.openGLControl1.Height * 3];
            gl.ReadPixels(0, 0, this.openGLControl1.Width, this.openGLControl1.Height, OpenGL.GL_RGB, OpenGL.GL_UNSIGNED_BYTE, pixelData);
            Console.WriteLine($"ReadPixels {pixelData.Length}.");
        }
    }
}
