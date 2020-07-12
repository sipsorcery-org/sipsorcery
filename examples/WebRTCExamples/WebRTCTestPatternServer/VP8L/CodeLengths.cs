namespace VP8L {
  using System;
  using System.Collections.Generic;

  internal static class CodeLengths {
    /// Encodes the given canonical Huffman code into the bitstream.
    internal static void WriteCodeLengths(BitWriter b, List<Huffman.Code> codes) {
      var simpleCodes = new List<Huffman.Code>();
      bool allSimple = true;
      foreach(var code in codes) {
        if(!code.Present) { continue; }
        if(code.Length > 1 || code.Symbol > 255) {
          allSimple = false;
          break;
        } else {
          simpleCodes.Add(code);
        }
      }

      if(allSimple) {
        WriteSimpleCodeLengths(b, simpleCodes);
      } else {
        WriteNormalCodeLengths(b, codes);
      }
    }

    /// If there are only at most two symbols in the code, it can be encoded
    /// efficiently.
    static void WriteSimpleCodeLengths(BitWriter b, List<Huffman.Code> codes) {
      b.WriteBits(1, 1);
      if(codes.Count == 0) {
        b.WriteBits(0, 3);
        return;
      }

      b.WriteBits(codes.Count - 1, 1);
      if(codes[0].Symbol <= 1) {
        b.WriteBits(0, 1);
        b.WriteBits(codes[0].Symbol, 1);
      } else {
        b.WriteBits(1, 1);
        b.WriteBits(codes[0].Symbol, 8);
      }
      if(codes.Count > 1) {
        b.WriteBits(codes[1].Symbol, 8);
      }
    }

    static readonly int[] CODE_LENGTH_ORDER = new int[] {
      17, 18, 0, 1, 2, 3, 4, 5, 16,
      6, 7, 8, 9, 10, 11, 12, 13, 14, 15 
    };

    /// If the size of the code is larger than two, we must transmit the length
    /// of every symbol in the code. To save space, the lengths are themselves
    /// Huffman-coded with a length code, so the lengths of the length code must
    /// be stored first.
    static void WriteNormalCodeLengths(BitWriter b, List<Huffman.Code> codes) {
      var encodedLengths = EncodeCodeLengths(codes);
      var lengthHisto = new Histogram(19);
      for(int i = 0; i < encodedLengths.Count; ++i) {
        int sym = encodedLengths[i];
        lengthHisto.Hit(sym);
        if(sym >= 16) { i += 1; }
      }
      var lengthCodes = Huffman.BuildCodes(lengthHisto, 7);

      int lengthCodeCount = 0;
      for(int i = 0; i < 19; ++i) {
        if(lengthHisto[CODE_LENGTH_ORDER[i]] > 0) {
          lengthCodeCount = i + 1;
        }
      }
      if(lengthCodeCount < 4) { lengthCodeCount = 4; }

      b.WriteBits(0, 1);
      b.WriteBits(lengthCodeCount - 4, 4);
      for(int i = 0; i < lengthCodeCount; ++i) {
        b.WriteBits(lengthCodes[CODE_LENGTH_ORDER[i]].Length, 3);
      }

      b.WriteBits(0, 1);
      for(int i = 0; i < encodedLengths.Count; ++i) {
        int sym = encodedLengths[i];
        b.WriteCode(lengthCodes[sym]);
        if(sym == 16) {
          b.WriteBits(encodedLengths[++i], 2);
        } else if(sym == 17) {
          b.WriteBits(encodedLengths[++i], 3);
        } else if(sym == 18) {
          b.WriteBits(encodedLengths[++i], 7);
        }
      }
    }

    /// Encodes the code into a sequence of codes from [0..18], where 16, 17 and
    /// 18 are special codes that encode a streak of zeros or a repetition.
    /// These three codes are followed with extra bits to determine the
    /// repetition length.
    static List<int> EncodeCodeLengths(List<Huffman.Code> codes) {
      var lengthCodes = new List<int>();
      int lastLength = 8;
      for(int sym = 0; sym < codes.Count; ++sym) {
        if(codes[sym].Length > 15) {
          throw new ArgumentException(
            String.Format("Code {0} is too long", codes[sym]), "codes");
        }

        if(codes[sym].Length == 0) {
          int streak = 1;
          while(streak < 138 && sym + streak < codes.Count) {
            if(codes[sym + streak].Length == 0) {
              streak += 1;
            } else {
              break;
            }
          }

          if(streak >= 11) {
            lengthCodes.Add(18);
            lengthCodes.Add(streak - 11);
            sym += streak - 1;
          } else if(streak >= 3) {
            lengthCodes.Add(17);
            lengthCodes.Add(streak - 3);
            sym += streak - 1;
          } else {
            lengthCodes.Add(0);
          }
          continue;
        }

        if(codes[sym].Length == lastLength) {
          int streak = 1;
          while(streak < 6 && sym + streak < codes.Count) {
            if(codes[sym + streak].Length == lastLength) {
              streak += 1;
            } else {
              break;
            }
          }

          if(streak >= 3) {
            lengthCodes.Add(16);
            lengthCodes.Add(streak - 3);
            sym += streak - 1;
            continue;
          }
        } else {
          lastLength = codes[sym].Length;
        }
        lengthCodes.Add(codes[sym].Length);
      }
      return lengthCodes;
    }
  }
}
