namespace VP8L {
  using System;
  using System.Collections.Generic;
  using System.Linq;

  internal static class Huffman {
    /// Given a histogram of symbols, computes the optimal canonical Huffman
    /// code for encoding these symbols.
    internal static List<Code> BuildCodes(Histogram histo, int maxLength) {
      var codes = new List<Code>();
      for(int sym = 0; sym < histo.Count; ++sym) {
        codes.Add(new Code(sym));
      }

      var tree = BuildTree(histo, maxLength);
      if(!tree.IsBranch) {
        // the special case of a singleton symbol (or no symbol at all) which is
        // present but has length 0
        var singletonSym = tree.LeafSymbol;
        codes[singletonSym] = new Code(singletonSym, 0, 0);
        return codes;
      }

      AssignCodeLengths(tree, 0, codes);
      var sorted = codes.ToList();
      sorted.Sort(new CodeComparer());

      // compute the canonical Huffman codes
      int bits = 0;
      int length = 0;
      foreach(var code in sorted) {
        if(!code.Present) { continue; }
        bits = bits << (code.Length - length);
        length = code.Length;
        codes[code.Symbol] = new Code(code.Symbol, bits, code.Length);
        bits += 1;
      }

      return codes;
    }

    /// A Huffman code representation of a single symbol.
    public struct Code {
      public bool Present;
      public int Symbol;
      public int Bits;
      public int Length;
      /// Construct a non-present code.
      public Code(int sym) {
        this.Present = false;
        this.Symbol = sym;
        this.Bits = 0;
        this.Length = 0;
      }
      /// Construct a present code. The length of a present code must be nonzero
      /// except the special case when only a single symbol is present.
      public Code(int sym, int bits, int length) {
        this.Present = true;
        this.Symbol = sym;
        this.Bits = bits;
        this.Length = length;
      }

      public override string ToString() {
        if(!this.Present) {
          return $"{this.Symbol} not present";
        }
        var code = "";
        for(int i = 0; i < this.Length; ++i) {
          code = ((this.Bits & (1 << i)) != 0 ? "1" : "0") + code;
        }
        return $"{this.Symbol} {code} [{this.Length}]";
      }
    }

    /// Sorts the codes in the canonical order of (length, symbol).
    private sealed class CodeComparer: Comparer<Code> {
      public override int Compare(Code c1, Code c2) {
        return c1.Length == c2.Length ? c1.Symbol - c2.Symbol : c1.Length - c2.Length;
      }
    }

    /// A node of a Huffman tree. Huffman trees are used only to compute the
    /// optimal code and are immediately discarded, as we are interested only in
    /// the code lengths.
    private sealed class Node {
      public bool IsBranch;
      public int Weight;
      public int LeafSymbol;
      public Node BranchLeft;
      public Node BranchRight;

      public Node(int symbol, int weight) {
        this.IsBranch = false;
        this.Weight = weight;
        this.LeafSymbol = symbol;
      }
      public Node(Node left, Node right) {
        this.IsBranch = true;
        this.Weight = left.Weight + right.Weight;
        this.BranchLeft = left;
        this.BranchRight = right;
      }
    }

    private sealed class NodeComparer: Comparer<Node> {
      public override int Compare(Node n1, Node n2) {
        return n1.Weight - n2.Weight;
      }
    }

    /// The Huffman code generation procedure.
    private static Node BuildTree(Histogram histo, int maxLength) {
      var minWeight = histo.Sum() >> (maxLength - 2);
      var heap = new Heap<Node>(new NodeComparer());
      for(int sym = 0; sym < histo.Count; ++sym) {
        var weight = histo[sym];
        if(weight == 0) { continue; }
        if(weight < minWeight) { weight = minWeight; }
        heap.Add(new Node(sym, weight));
      }

      while(heap.Count > 1) {
        var n1 = heap.RemoveMin();
        var n2 = heap.RemoveMin();
        heap.Add(new Node(n1, n2));
      }
      if(heap.Count > 0) {
        return heap.Min;
      } else {
        return new Node(0, 0);
      }
    }

    private static void AssignCodeLengths(Node node, int depth, List<Code> codes) {
      if(node.IsBranch) {
        AssignCodeLengths(node.BranchLeft, depth + 1, codes);
        AssignCodeLengths(node.BranchRight, depth + 1, codes);
      } else {
        codes[node.LeafSymbol] = new Code(node.LeafSymbol, 0, depth);
      }
    }
  }
}
