namespace VP8L {
  using System;
  using System.Collections.Generic;
  using System.Linq;

  /// The image transforms that can be used to lower the entropy of the image.
  /// All the transform functions take the input images as argument and return
  /// the transformed image. They are permitted to mutate the image and can
  /// return either the same image or a new instance.
  internal static class Transform {
    /// The palette (color-indexing) transform replaces pixels with indices into
    /// a palette that is stored before the image data. If the palette is
    /// sufficiently small, multiple indices are packed into a single pixel.
    internal static Image PaletteTransform(BitWriter b, Image image, Palette palette) {
      b.WriteBits(1, 1);
      b.WriteBits(3, 2);
      WritePalette(b, palette);

      int packSize = 
        palette.Count <= 2 ? 8 :
        palette.Count <= 4 ? 4 :
        palette.Count <= 16 ? 2 : 1;

      int packedWidth = (image.Width + packSize - 1) / packSize;
      var palettized = new Image(packedWidth, image.Height);
      for(int y = 0; y < image.Height; ++y) {
        for(int i = 0; i < packedWidth; ++i) {
          int pack = 0;
          for(int j = 0; j < packSize; ++j) {
            int x = i * packSize + j;
            if(x >= image.Width) { break; }
            int colorIndex = palette.Indices[image[x, y]];
            pack |= colorIndex << (j * (8 / packSize));
          }
          palettized[i, y] = new Argb(255, 0, (byte)pack, 0);
        }
      }

      return palettized;
    }

    /// The palette is encoded as an image with height 1.
    static void WritePalette(BitWriter b, Palette palette) {
      var image = new Image(palette.Count, 1);
      for(int i = 0; i < palette.Count; ++i) {
        image.Pixels[i] = i == 0 ? palette.Colors[0] 
          : palette.Colors[i] - palette.Colors[i-1];
      }

      b.WriteBits(palette.Count - 1, 8);
      ImageData.WriteImageData(b, image, false);
    }

    /// The subtract-green transform just subtracts the value of the green
    /// channel from the red and blue channels, which usually decreases entropy
    /// in those channels.
    internal static Image SubtractGreenTransform(BitWriter b, Image image) {
      b.WriteBits(1, 1);
      b.WriteBits(2, 2);
      for(int i = 0; i < image.Pixels.Length; ++i) {
        var argb = image.Pixels[i];
        argb.R = (byte)(argb.R - argb.G);
        argb.B = (byte)(argb.B - argb.G);
        image.Pixels[i] = argb;
      }
      return image;
    }

    /// The prediction transform predicts the values of pixels using their
    /// already decoded neighbors, storing only the difference between the
    /// predicted and actual value. There are 14 prediction modes and which mode
    /// is used is determined from an encoded subsample image.
    internal static Image PredictTransform(BitWriter b, Image image) {
      b.WriteBits(1, 1);
      b.WriteBits(0, 2);

      int tileBits = 4;
      int tileSize = 0;
      int blockedWidth = 0;
      int blockedHeight = 0;
      while(tileBits < 2 + 8) {
        tileSize = 1 << tileBits;
        blockedWidth = (image.Width + tileSize - 1) / tileSize;
        blockedHeight = (image.Height + tileSize - 1) / tileSize;
        if(blockedWidth * blockedHeight < 2000) { break; }
        ++tileBits;
      }

      b.WriteBits(tileBits - 2, 3);
      var blocks = new Image(blockedWidth, blockedHeight);
      var residuals = new Image(image.Width, image.Height);
      var accumHistos = Enumerable.Range(0, 4).Select(_ => new Histogram(256)).ToList();
      for(int y = 0; y < blockedHeight; ++y) {
        for(int x = 0; x < blockedWidth; ++x) {
          int bestPrediction = 0;
          double bestEntropy = PredictEntropy(image, tileBits, x, y, 0, accumHistos);
          for(int i = 1; i < PREDICTIONS.Count; ++i) {
            double entropy = PredictEntropy(image, tileBits, x, y, i, accumHistos);
            if(entropy < bestEntropy) {
              bestPrediction = i;
              bestEntropy = entropy;
            }
          }

          blocks[x, y] = new Argb(255, 0, (byte)bestPrediction, 0);
          PredictBlock(image, residuals, tileBits, x, y, bestPrediction, accumHistos);
        }
      }

      ImageData.WriteImageData(b, blocks, false);
      return residuals;
    }

    /// Computes the entropy of the residuals in the given tile of the image
    /// using the specified prediction mode. The entropy is computed with
    /// respect to the histograms of the already encoded residuals.
    static double PredictEntropy(Image image, int tileBits,
        int tileX, int tileY, int prediction, List<Histogram> accumHistos)
    {
      var histos = accumHistos.Select(histo => new Histogram(histo)).ToList();
      int maxX = Min((tileX + 1) << tileBits, image.Width);
      int maxY = Min((tileY + 1) << tileBits, image.Height);
      for(int x = tileX << tileBits; x < maxX; ++x) {
        for(int y = tileY << tileBits; y < maxY; ++y) {
          var delta = image[x, y] - Predict(image, x, y, prediction);
          histos[0].Hit(delta.A);
          histos[1].Hit(delta.R);
          histos[2].Hit(delta.G);
          histos[3].Hit(delta.B);
        }
      }
      return histos.Select(histo => histo.Entropy()).Sum();
    }

    /// Stores the residuals of the prediction of the given tile and updates the
    /// histograms.
    static void PredictBlock(Image image, Image residuals, int tileBits,
        int tileX, int tileY, int prediction, List<Histogram> histos)
    {
      int maxX = Min((tileX + 1) << tileBits, image.Width);
      int maxY = Min((tileY + 1) << tileBits, image.Height);
      for(int x = tileX << tileBits; x < maxX; ++x) {
        for(int y = tileY << tileBits; y < maxY; ++y) {
          var delta = image[x, y] - Predict(image, x, y, prediction);
          histos[0].Hit(delta.A);
          histos[1].Hit(delta.R);
          histos[2].Hit(delta.G);
          histos[3].Hit(delta.B);
          residuals[x, y] = delta;
        }
      }
    }

    /// Computes the predicted value for a single image pixel given the
    /// prediction mode.
    static Argb Predict(Image image, int x, int y, int prediction) {
      if(x == 0 && y == 0) {
        return new Argb(255, 0, 0, 0);
      } else if(x == 0) {
        return image[x, y - 1];
      } else if(y == 0) {
        return image[x - 1, y];
      }

      int i = y * image.Width + x;
      Argb top = image.Pixels[i - image.Width];
      Argb left = image.Pixels[i - 1];
      Argb topLeft = image.Pixels[i - image.Width - 1];
      Argb topRight = image.Pixels[i - image.Width + 1];
      return PREDICTIONS[prediction](top, left, topLeft, topRight);
    }

    delegate Argb Prediction(Argb top, Argb left, Argb topLeft, Argb topRight);
    static readonly List<Prediction> PREDICTIONS = new List<Prediction> {
      (t,l,tl,tr) => new Argb(255, 0, 0, 0),
      (t,l,tl,tr) => l,
      (t,l,tl,tr) => t,
      (t,l,tl,tr) => tr,
      (t,l,tl,tr) => tl,
      (t,l,tl,tr) => Average2(Average2(l, tr), t),
      (t,l,tl,tr) => Average2(l, tl),
      (t,l,tl,tr) => Average2(l, t),
      (t,l,tl,tr) => Average2(tl, t),
      (t,l,tl,tr) => Average2(t, tr),
      (t,l,tl,tr) => Average2(Average2(l, tl), Average2(t, tr)),
      (t,l,tl,tr) => Select(l, t, tl),
      (t,l,tl,tr) => ClampAddSubtractFull(l, t, tl),
      (t,l,tl,tr) => ClampAddSubtractHalf(Average2(l, t), tl),
    };

    static Argb Average2(Argb a, Argb b) {
      return new Argb(
          (byte)((a.A + b.A) / 2),
          (byte)((a.R + b.R) / 2),
          (byte)((a.G + b.G) / 2),
          (byte)((a.B + b.B) / 2));
    }
    static Argb Select(Argb l, Argb t, Argb tl) {
      int pA = l.A + t.A - tl.A;
      int pR = l.R + t.R - tl.R;
      int pG = l.G + t.G - tl.G;
      int pB = l.B + t.B - tl.B;

      int pL = Abs(pA - l.A) + Abs(pR - l.R) + Abs(pG - l.G) + Abs(pB - l.B);
      int pT = Abs(pA - t.A) + Abs(pR - t.R) + Abs(pG - t.G) + Abs(pB - t.B);
      return pL < pT ? l : t;
    }
    static Argb ClampAddSubtractFull(Argb l, Argb t, Argb tl) {
      return new Argb(
          Clamp(l.A + t.A - tl.A),
          Clamp(l.R + t.R - tl.R),
          Clamp(l.G + t.G - tl.G),
          Clamp(l.B + t.B - tl.B));
    }
    static Argb ClampAddSubtractHalf(Argb a, Argb b) {
      return new Argb(
        Clamp(a.A + (a.A - b.A) / 2),
        Clamp(a.R + (a.R - b.R) / 2),
        Clamp(a.G + (a.G - b.G) / 2),
        Clamp(a.B + (a.B - b.B) / 2));
    }
    static int Abs(int a) {
      return a >= 0 ? a : -a;
    }
    static byte Clamp(int a) {
      return a <= 0 ? (byte)0 : a >= 256 ? (byte)255 : (byte)a;
    }
    static int Min(int a, int b) {
      return a < b ? a : b;
    }
  }
}
