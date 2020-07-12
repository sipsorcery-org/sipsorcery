namespace VP8L {
  using System;
  using System.Collections.Generic;
  using System.IO;

  /// The top-level format functions.
  public static class Format {
    /// Encodes the image as a VP8L stream inside the WebP lossless container.
    public static void Encode(BinaryWriter w, Image image) {
      var b = new BitWriter();
      WriteImageBitstream(b, image);
      b.AlignByte();
      if(b.ByteBuffer.Count % 2 != 0) {
        b.ByteBuffer.Add(0);
      }
      WriteWebPContainer(w, b.ByteBuffer);
    }

    /// Wraps the raw VP8L bitstream data in the WebP container.
    static void WriteWebPContainer(BinaryWriter w, List<Byte> data) {
      if(data.Count % 2 != 0) {
        throw new ArgumentException("Number of bytes must be even", "data");
      }
      w.Write(new char[]{'R','I','F','F'});
      w.Write((uint)(12 + data.Count));
      w.Write(new char[]{'W','E','B','P'});
      w.Write(new char[]{'V','P','8','L'});
      w.Write((uint)data.Count);
      w.Write(data.ToArray());
    }

    /// Encodes the image as a raw VP8L bitstream.
    static void WriteImageBitstream(BitWriter b, Image image) {
      var analysis = Analysis.AnalyzeImage(image);
      WriteHeader(b, image.Width, image.Height, analysis.HasAlpha);

      if(analysis.PaletteOrNull != null) {
        image = Transform.PaletteTransform(b, image, analysis.PaletteOrNull);
      }
      if(analysis.UseSubtractGreen) {
        image = Transform.SubtractGreenTransform(b, image);
      }
      if(analysis.UsePredict) {
        image = Transform.PredictTransform(b, image);
      }
      b.WriteBits(0, 1);

      ImageData.WriteImageData(b, image, true, analysis.ColorCacheBits);
    }

    static void WriteHeader(BitWriter b, int width, int height, bool hasAlpha) {
      b.WriteBits(0x2f, 8); // signature
      b.WriteBits(width - 1, 14);
      b.WriteBits(height - 1, 14);
      b.WriteBits(hasAlpha ? 1 : 0, 1);
      b.WriteBits(0, 3); // version 0
    }
  }
}
