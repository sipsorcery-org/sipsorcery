namespace VP8L {
  using System;
  using System.Collections.Generic;
  using System.Linq;

  /// A palette that is used to replace pixel values with indices into the
  /// palette.
  internal class Palette {
    internal List<Argb> Colors = new List<Argb>();
    internal Dictionary<Argb, int> Indices = new Dictionary<Argb, int>();
    internal int Count => this.Colors.Count;
  }

  /// The result of analysing an image.
  internal class ImageAnalysis {
    internal bool HasAlpha;
    internal Palette PaletteOrNull;
    internal bool UseSubtractGreen;
    internal bool UsePredict;
    internal int ColorCacheBits;
  }

  internal static class Analysis {
    /// Histograms of various pixel quantities, which are used to estimate the
    /// entropy of different transforms.
    enum HistogramIdx: int {
      // direct values
      RED, GREEN, BLUE, ALPHA,
      // values of the differences between pixels, used to estimate the entropy
      // of prediction residuals
      DELTA_RED, DELTA_GREEN, DELTA_BLUE, DELTA_ALPHA,
      // values of red and blue channels after the subtract-green transform
      RED_MINUS_GREEN, BLUE_MINUS_GREEN,
      // values of the differences after the subtract-green transform
      DELTA_RED_MINUS_DELTA_GREEN, DELTA_BLUE_MINUS_DELTA_GREEN,
      // values of palette indices
      PALETTE,
      _COUNT,
    }

    /// The various sequences of transforms that can be used.
    enum TransformSet: int {
      NOTHING,
      PALETTE,
      SUBGREEN,
      PREDICT,
      SUBGREEN_PREDICT,
      _COUNT,
    }

    /// Analyzes the entropy in the image and estimates which transforms should
    /// be used.
    internal static ImageAnalysis AnalyzeImage(Image image) {
      bool hasAlpha;
      var palette = GeneratePaletteOrNull(image);
      var histos = ComputeHistograms(image, palette, out hasAlpha);
      var entropies = histos.Select(histo => histo.Entropy()).ToList();
      var transformEntropies = ComputeTransformEntropies(entropies);

      var best = TransformSet.NOTHING;
      for(int i = 1; i < (int)TransformSet._COUNT; ++i) {
        if(i == (int)TransformSet.PALETTE && palette == null) { continue; }
        if(transformEntropies[i] < transformEntropies[(int)best]) {
          best = (TransformSet)i;
        }
      }

      var analysis = new ImageAnalysis();
      analysis.HasAlpha = hasAlpha;
      analysis.PaletteOrNull = 
        best == TransformSet.PALETTE ? palette : null;
      analysis.UseSubtractGreen =
        best == TransformSet.SUBGREEN || best == TransformSet.SUBGREEN_PREDICT;
      analysis.UsePredict =
        best == TransformSet.PREDICT || best == TransformSet.SUBGREEN_PREDICT;
      analysis.ColorCacheBits = 0;
      return analysis;
    }

    /// Computes all the histograms from an image.
    static List<Histogram> ComputeHistograms(Image image,
        Palette paletteOrNull, out bool hasAlpha) 
    {
      var histos = Enumerable.Range(0, (int)HistogramIdx._COUNT)
        .Select(_ => new Histogram(256)).ToList();

      Argb previous = new Argb(255, 0, 0, 0);
      hasAlpha = false;
      for(int i = 0; i < image.Pixels.Length; ++i) {
        Argb pixel = image.Pixels[i];

        if(pixel.A != 255) { hasAlpha = true; }
        Argb delta = pixel - previous;
        previous = pixel;

        histos[(int)HistogramIdx.RED].Hit(pixel.R);
        histos[(int)HistogramIdx.GREEN].Hit(pixel.G);
        histos[(int)HistogramIdx.BLUE].Hit(pixel.B);
        histos[(int)HistogramIdx.ALPHA].Hit(pixel.A);

        histos[(int)HistogramIdx.DELTA_RED].Hit(delta.R);
        histos[(int)HistogramIdx.DELTA_GREEN].Hit(delta.G);
        histos[(int)HistogramIdx.DELTA_BLUE].Hit(delta.B);
        histos[(int)HistogramIdx.DELTA_ALPHA].Hit(delta.A);

        histos[(int)HistogramIdx.RED_MINUS_GREEN].Hit((byte)(pixel.R - pixel.G));
        histos[(int)HistogramIdx.BLUE_MINUS_GREEN].Hit((byte)(pixel.B - pixel.G));
        histos[(int)HistogramIdx.DELTA_RED_MINUS_DELTA_GREEN]
          .Hit((byte)(delta.R - delta.G));
        histos[(int)HistogramIdx.DELTA_BLUE_MINUS_DELTA_GREEN]
          .Hit((byte)(delta.B - delta.G));

        if(paletteOrNull != null) {
          histos[(int)HistogramIdx.PALETTE].Hit(paletteOrNull.Indices[pixel]);
        }
      }
      return histos;
    }

    /// Computes the entropy estimates for the transform modes from the
    /// histogram entropies.
    static List<double> ComputeTransformEntropies(List<double> entropies) {
      return new List<double> {
        entropies[(int)HistogramIdx.RED] +
        entropies[(int)HistogramIdx.GREEN] +
        entropies[(int)HistogramIdx.BLUE] +
        entropies[(int)HistogramIdx.ALPHA],

        entropies[(int)HistogramIdx.PALETTE],

        entropies[(int)HistogramIdx.RED_MINUS_GREEN] +
        entropies[(int)HistogramIdx.GREEN] +
        entropies[(int)HistogramIdx.BLUE_MINUS_GREEN] +
        entropies[(int)HistogramIdx.ALPHA],

        entropies[(int)HistogramIdx.DELTA_RED] +
        entropies[(int)HistogramIdx.DELTA_GREEN] +
        entropies[(int)HistogramIdx.DELTA_BLUE] +
        entropies[(int)HistogramIdx.DELTA_ALPHA],

        entropies[(int)HistogramIdx.DELTA_RED_MINUS_DELTA_GREEN] +
        entropies[(int)HistogramIdx.DELTA_GREEN] +
        entropies[(int)HistogramIdx.DELTA_BLUE_MINUS_DELTA_GREEN] +
        entropies[(int)HistogramIdx.DELTA_ALPHA],
      };
    }

    /// Computes a palette of the image if the number of colors is not too high.
    static Palette GeneratePaletteOrNull(Image image) {
      var palette = new Palette();
      foreach(Argb color in image.Pixels) {
        if(palette.Indices.ContainsKey(color)) {
          continue;
        }
        palette.Indices.Add(color, palette.Colors.Count);
        palette.Colors.Add(color);
        if(palette.Colors.Count > 256) {
          return null;
        }
      }
      return palette;
    }

  }
}
