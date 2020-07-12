namespace VP8L {
  public class Image {
    public Argb[] Pixels;
    public int Width { get; }
    public int Height { get; }

    public Image(int width, int height) {
      this.Pixels = new Argb[height * width];
      this.Width = width;
      this.Height = height;
    }
    public Image(System.Drawing.Bitmap bitmap) {
      this.Pixels = new Argb[bitmap.Height * bitmap.Width];
      this.Width = bitmap.Width;
      this.Height = bitmap.Height;
      for(int y = 0; y < bitmap.Height; ++y) {
        for(int x = 0; x < bitmap.Width; ++x) {
          this.Pixels[y * bitmap.Width + x] = new Argb(bitmap.GetPixel(x, y));
        }
      }
    }

    public Argb this[int x, int y] {
      get { return this.Pixels[y * this.Width + x]; }
      set { this.Pixels[y * this.Width + x] = value; }
    }
  }
}
