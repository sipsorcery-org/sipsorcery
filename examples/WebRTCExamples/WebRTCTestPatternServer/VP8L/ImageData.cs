namespace VP8L {
  using System;
  using System.Collections.Generic;
  using System.Linq;

  /// Functions concerning the entropy-coded image data (either the main ARGB
  /// image or the embedded subsample images for transforms).
  internal static class ImageData {
    /// Encodes the image into entropy-coded bitstream. The isRecursive flag
    /// must be true iff the data represents the main ARGB image.
    internal static void WriteImageData(BitWriter b, Image image,
        bool isRecursive, int colorCacheBits = 0)
    {
      if(colorCacheBits > 0) {
        if(colorCacheBits > 11) {
          throw new ArgumentException("Too many color cache bits");
        }
        b.WriteBits(1, 1);
        b.WriteBits(colorCacheBits, 4);
      } else {
        b.WriteBits(0, 1);
      }

      if(isRecursive) {
        b.WriteBits(0, 1); // no meta-Huffman image
      }

      var encoded = EncodeImageData(image, colorCacheBits);
      var histos = new List<Histogram> {
        new Histogram(256 + 24 + (colorCacheBits > 0 ? (1 << colorCacheBits) : 0)),
        new Histogram(256),
        new Histogram(256),
        new Histogram(256),
        new Histogram(40),
      };
      for(int i = 0; i < encoded.Count; ++i) {
        histos[0].Hit(encoded[i]);
        if(encoded[i] < 256) {
          histos[1].Hit(encoded[i+1]);
          histos[2].Hit(encoded[i+2]);
          histos[3].Hit(encoded[i+3]);
          i += 3;
        } else if(encoded[i] < 256 + 24) {
          histos[4].Hit(encoded[i+2]);
          i += 3;
        }
      }

      var codess = new List<List<Huffman.Code>>();
      for(int i = 0; i < 5; ++i) {
        var codes = Huffman.BuildCodes(histos[i], 16);
        CodeLengths.WriteCodeLengths(b, codes);
        codess.Add(codes);
      }

      for(int i = 0; i < encoded.Count; ++i) {
        b.WriteCode(codess[0][encoded[i]]);
        if(encoded[i] < 256) {
          b.WriteCode(codess[1][encoded[i+1]]);
          b.WriteCode(codess[2][encoded[i+2]]);
          b.WriteCode(codess[3][encoded[i+3]]);
          i += 3;
        } else if(encoded[i] < 256 + 24) {
          b.WriteBits(encoded[i+1], ExtraBitsCount(encoded[i] - 256));
          b.WriteCode(codess[4][encoded[i+2]]);
          b.WriteBits(encoded[i+3], ExtraBitsCount(encoded[i+2]));
          i += 3;
        }
      }
    }

    internal class Chain {
      internal Chain NextOrNull;
      internal int Index;
      internal Chain(Chain next, int index) {
        this.NextOrNull = next;
        this.Index = index;
      }
    }

    /// A simple hash table that stores the occurence of every triple of
    /// successive ARGB values in the image. This structure is used to find
    /// backward references.
    internal class ChainTable {
      private List<Chain> Chains;
      internal ChainTable(int size) {
        this.Chains = new List<Chain>(size);
        for(int i = 0; i < size; ++i) {
          this.Chains.Add(null);
        }
      }

      internal Chain GetChain(Argb a1, Argb a2, Argb a3) {
        return this.Chains[(int)(Argb.Hash(a1, a2, a3) % this.Chains.Count())];
      }
      internal void AddChain(Argb a1, Argb a2, Argb a3, int index) {
        int i = (int)(Argb.Hash(a1, a2, a3) % this.Chains.Count());
        this.Chains[i] = new Chain(this.Chains[i], index);
      }
    }

    /// The color cache stores recently used colors which can later be referred
    /// to by the index in the cache.
    internal class ColorCache {
      private int Bits;
      private Argb[] Array;
      internal ColorCache(int bits) {
        this.Bits = bits;
        this.Array = new Argb[1 << bits];
      }

      internal bool Present => this.Bits > 0;
      internal bool Lookup(Argb color, out int index) {
        if(this.Bits <= 0) {
          index = 0; return false;
        }
        index = this.Index(color);
        return this.Array[index] == color;
      }
      internal void Insert(Argb color) {
        if(this.Bits > 0) {
          this.Array[this.Index(color)] = color;
        }
      }
      private int Index(Argb color) {
        return (int)((uint)(color.ToUInt() * 0x1e35a7bd) >> (32 - this.Bits));
      }
    }

    /// Encodes the pixels in the image as a linear sequence of codes. Because
    /// C# simply lacks any data structure that could be used to represent the
    /// three types of codes (literals, backward references and color cache
    /// indices), we encode the sequence of codes as a simple list of integers.
    ///
    /// The codes are stored in the sequence as follows:
    /// 1. [green, red, blue, alpha] is a literal
    /// 2. [length code + 256, length extra bits, distance code, distance extra bits]
    /// 3. [color cache index + 256 + 24]
    /// with the type determined by the first element.
    static List<int> EncodeImageData(Image image, int colorCacheBits) {
      var chainTable = new ChainTable(20*1000);
      var colorCache = new ColorCache(colorCacheBits);

      var encoded = new List<int>();
      int pixelCount = image.Pixels.Count();
      for(int pix = 0; pix < pixelCount; ++pix) {
        var argb = image.Pixels[pix];

        if(pix + 2 < pixelCount) {
          var chain = chainTable.GetChain(argb,
              image.Pixels[pix+1], image.Pixels[pix+2]);
          int longestIndex = 0;
          int longestLength = 0;
          for(int i = 0; i < 100 && chain != null; ++i) {
            // there is a limit to the distance that can be encoded
            if(pix - chain.Index > 1048576 - 120) { break; }
            int length = FindMatchLength(image, pix, chain.Index, 4096);
            if(length > longestLength) {
              longestIndex = chain.Index;
              longestLength = length;
            }
            chain = chain.NextOrNull;
          }

          if(longestLength >= 3) {
            int lengthExtra, distanceExtra;
            int distanceCode = DistanceCode(image, pix - longestIndex);
            int lengthSymbol = PrefixCode(longestLength, out lengthExtra);
            int distanceSymbol = PrefixCode(distanceCode, out distanceExtra);

            if(colorCache.Present) {
              for(int i = 0; i < longestLength; ++i) {
                colorCache.Insert(image.Pixels[pix + i]);
              }
            }

            encoded.Add(lengthSymbol + 256);
            encoded.Add(lengthExtra);
            encoded.Add(distanceSymbol);
            encoded.Add(distanceExtra);
            pix += longestLength - 1;
            continue;
          }

          chainTable.AddChain(argb, image.Pixels[pix+1], image.Pixels[pix+2], pix);
        }

        int colorCacheIndex;
        if(colorCache.Lookup(argb, out colorCacheIndex)) {
          encoded.Add(colorCacheIndex + 256 + 24);
          continue;
        }

        encoded.Add(argb.G);
        encoded.Add(argb.R);
        encoded.Add(argb.B);
        encoded.Add(argb.A);
        colorCache.Insert(argb);
      }
      return encoded;
    }

    static int FindMatchLength(Image image, int pix, int matchIndex, int maxLength) {
      int i = 0;
      for(; pix + i < image.Width * image.Height && i < maxLength; ++i) {
        if(image.Pixels[pix + i] != image.Pixels[matchIndex + i]) { break; }
      }
      return i;
    }

    static readonly int[] DISTANCE_CODES = new int[128] {
      96,   73,  55,  39,  23,  13,   5,  1,  255, 255, 255, 255, 255, 255, 255, 255,
      101,  78,  58,  42,  26,  16,   8,  2,    0,   3,  9,   17,  27,  43,  59,  79,
      102,  86,  62,  46,  32,  20,  10,  6,    4,   7,  11,  21,  33,  47,  63,  87,
      105,  90,  70,  52,  37,  28,  18,  14,  12,  15,  19,  29,  38,  53,  71,  91,
      110,  99,  82,  66,  48,  35,  30,  24,  22,  25,  31,  36,  49,  67,  83, 100,
      115, 108,  94,  76,  64,  50,  44,  40,  34,  41,  45,  51,  65,  77,  95, 109,
      118, 113, 103,  92,  80,  68,  60,  56,  54,  57,  61,  69,  81,  93, 104, 114,
      119, 116, 111, 106,  97,  88,  84,  74,  72,  75,  85,  89,  98, 107, 112, 117,
    };

    /// The distance codes for spatially close pixels are encoded specially
    /// using a lookup table.
    static int DistanceCode(Image image, int dist) {
      int distY = dist / image.Width;
      int distX = dist - distY * image.Width;
      if(distX <= 8 && distY < 8) {
        return DISTANCE_CODES[distY*16 + 8 - distX] + 1;
      } else if(distX > image.Width - 8 && distY < 7) {
        return DISTANCE_CODES[(distY+1)*16 + 8 + (image.Width - distX)] + 1;
      } else {
        return dist + 120;
      }
    }
    // (the code of DistanceCode and the table DISTANCE_CODES are shamelessly
    // taken from the libwebp sources)

    /// The length and distance codes are encoded using a small symbol (below 24
    /// for lengths and 40 for distances), encoded using a Huffman code,
    /// followed by a variable number of extra bits (determined from the
    /// symbol).
    static int PrefixCode(int number, out int extra) {
      if(number <= 5) {
        extra = 0;
        return number - 1;
      }

      int rem = number - 1;
      int shift = 0;
      while(rem > 3) {
        rem >>= 1;
        shift += 1;
      }

      if(rem == 2) {
        extra = number - (2 << shift) - 1;
        return 2 + 2 * shift;
      } else if(rem == 3) {
        extra = number - (3 << shift) - 1;
        return 3 + 2 * shift;
      } else {
        throw new ArgumentException("Bad number");
      }
    }

    static int ExtraBitsCount(int prefixCode) {
      return prefixCode < 4 ? 0 : (prefixCode - 2) >> 1;
    }
  }
}
