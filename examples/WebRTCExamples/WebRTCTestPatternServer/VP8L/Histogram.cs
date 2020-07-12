namespace VP8L {
  using System;
  using System.Linq;

  internal sealed class Histogram {
    private int[] Bins;
    internal Histogram(int count) {
      this.Bins = new int[count];
    }
    internal Histogram(Histogram histo) {
      this.Bins = histo.Bins.ToArray();
    }

    internal int this[int i] {
      get { return this.Bins[i]; }
      set { this.Bins[i] = value; }
    }
    internal int Count =>
      this.Bins.Length;

    internal void Hit(int i) {
      this.Bins[i] += 1;
    }
    internal int Sum() {
      int sum = 0;
      foreach(int x in this.Bins) {
        sum += x;
      }
      return sum;
    }
    internal int NonzeroCount() {
      int count = 0;
      foreach(int x in this.Bins) {
        if(x != 0) { count += 1; }
      }
      return count;
    }
    internal double Entropy() {
      double sum = (double)this.Sum();
      if(sum == 0.0) { return 0.0; }
      double logSum = Math.Log(sum);

      double sumLogs = 0.0;
      foreach(int x in this.Bins) {
        if(x == 0) { continue; }
        sumLogs += (double)x * (Math.Log((double)x) - logSum);
      }
      return -sumLogs / sum / Math.Log(2.0);
    }
  }
}
