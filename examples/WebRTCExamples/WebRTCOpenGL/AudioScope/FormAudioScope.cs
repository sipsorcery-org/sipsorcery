using System;
using System.Numerics;
using System.Windows.Forms;
using SharpGL;

namespace AudioScope
{
    public partial class FormAudioScope : Form
    {
        public const int AUDIO_SCOPE_WIDTH = 640;
        public const int AUDIO_SCOPE_HEIGHT = 480;

        private AudioScope _audioScope;
        private AudioScopeOpenGL _audioScopeGL;
        private byte[] _pixelData;

        public event EventHandler<byte[]> OnFrameReady;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;

                // Remove WS_EX_APPWINDOW to ensure it won't appear in Alt+Tab
                cp.ExStyle &= ~0x00040000; // WS_EX_APPWINDOW

                // Add WS_EX_TOOLWINDOW to hide from Alt+Tab
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW

                // Ensure the window is layered (required for transparency)
                cp.ExStyle |= 0x00080000; // WS_EX_LAYERED

                // Make it transparent to mouse events
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT

                return cp;
            }
        }

        public FormAudioScope()
        {
            _audioScope = new AudioScope();
            _audioScopeGL = new AudioScopeOpenGL(_audioScope);

            _pixelData = new byte[AUDIO_SCOPE_WIDTH * AUDIO_SCOPE_HEIGHT * 3];

            InitializeComponent();

            this.ShowInTaskbar = false;        // remove from the taskbar
            this.FormBorderStyle = FormBorderStyle.None;
            this.Opacity = 0;                 // invisible, but "active" 
            this.StartPosition = FormStartPosition.Manual;
            this.Enabled = false;
        }

        //public void RequestRender()
        //{
        //    // Must be on UI thread, so use Invoke if called from another thread
        //    if (this.InvokeRequired)
        //    {
        //        this.Invoke(() => openGLControl1.DoRender());
        //    }
        //    else
        //    {
        //        openGLControl1.DoRender();
        //    }
        //}

        public byte[] ProcessAudioSample(Complex[] samples)
        {
            _audioScope.ProcessSample(samples);

            openGLControl1.DoRender();

            this.openGLControl1.OpenGL.ReadPixels(0, 0, AUDIO_SCOPE_WIDTH, AUDIO_SCOPE_HEIGHT, OpenGL.GL_RGB, OpenGL.GL_UNSIGNED_BYTE, _pixelData);

            return _pixelData;
        }

        private void FormAudioScope_Load(object sender, EventArgs e)
        {
            //_audioScope.InitAudio(AudioSourceEnum.NAudio);
            //_audioScope.Start();
        }

        private void OpenGLControl_OpenGLInitialized(object sender, EventArgs e)
        {
            OpenGL gl = this.openGLControl1.OpenGL;
            _audioScopeGL.Initialise(gl);
        }

        private void openGLControl1_OpenGLDraw(object sender, RenderEventArgs e)
        {
            OpenGL gl = this.openGLControl1.OpenGL;
            _audioScopeGL.Draw(gl, AUDIO_SCOPE_WIDTH, AUDIO_SCOPE_HEIGHT);

            gl.Finish();
            //gl.ReadPixels(0, 0, AUDIO_SCOPE_WIDTH, AUDIO_SCOPE_HEIGHT, OpenGL.GL_RGB, OpenGL.GL_UNSIGNED_BYTE, _pixelData);

            //Console.WriteLine($"ReadPixels {Convert.ToBase64String(SHA256.HashData(_pixelData))}.");

            //OnFrameReady?.Invoke(this, _pixelData);
        }
    }
}
