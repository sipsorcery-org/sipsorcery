namespace VP8L {
  using System;
  using System.Collections.Generic;

  /// Helper object to generate a bitstream.
  internal sealed class BitWriter {
    internal List<Byte> ByteBuffer = new List<Byte>();
    private UInt64 BitBuffer = 0;
    private int BitBufferSize = 0;

    internal void WriteBits(int bits, int count) {
      if(bits < 0 || bits >= (1 << count)) {
        throw new ArgumentException("too many bits", "bits");
      }
      this.WriteThrough();
      this.BitBuffer |= (UInt64)(bits << this.BitBufferSize);
      this.BitBufferSize += count;
    }
    internal void WriteCode(Huffman.Code code) {
      for(int i = code.Length; i-- > 0;) {
        this.WriteBits(((code.Bits & (1 << i)) == 0) ? 0 : 1, 1);
      }
    }
    /// Ensures that all written bits appear in the ByteBuffer.
    internal void AlignByte() {
      this.BitBufferSize = (this.BitBufferSize + 7) & ~7;
      this.WriteThrough();
    }

    private void WriteThrough() {
      while(this.BitBufferSize >= 8) {
        this.ByteBuffer.Add((Byte)(this.BitBuffer & 0xff));
        this.BitBuffer >>= 8;
        this.BitBufferSize -= 8;
      }
    }
  }
}
