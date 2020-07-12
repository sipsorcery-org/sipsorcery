namespace VP8L {
  using System;
  using System.Collections.Generic;

  internal sealed class Heap<T> {
    private IComparer<T> Comparer;
    private List<T> Array;

    public Heap() {
      this.Comparer = Comparer<T>.Default;
      this.Array = new List<T>();
    }

    public Heap(IComparer<T> comparer) {
      this.Comparer = comparer;
      this.Array = new List<T>();
    }

    public void Add(T elem) {
      this.Array.Add(elem);
      this.BubbleUp(this.Array.Count - 1);
    }

    public T RemoveMin() {
      T min = this.Array[0];
      this.Array[0] = this.Array[this.Array.Count - 1];
      this.Array.RemoveAt(this.Array.Count - 1);
      this.BubbleDown(0);
      return min;
    }

    public int Count {
      get { return this.Array.Count; }
    }

    public T Min {
      get { return this.Array[0]; }
    }

    private void BubbleUp(int idx) {
      if(idx <= 0) {
        return;
      }
      int parentIdx = (idx - 1) / 2;
      T elem = this.Array[idx];
      T parent = this.Array[parentIdx];
      if(this.Comparer.Compare(parent, elem) > 0) {
        this.Array[parentIdx] = elem;
        this.Array[idx] = parent;
        this.BubbleUp(parentIdx);
      }
    }

    private void BubbleDown(int idx) {
      if(idx >= this.Array.Count) {
        return;
      }
      T elem = this.Array[idx];
      int leftIdx = idx * 2 + 1;
      int rightIdx = idx * 2 + 2;
      if(rightIdx < this.Array.Count) {
        T left = this.Array[leftIdx];
        T right = this.Array[rightIdx];
        if(this.Comparer.Compare(left, right) < 0) {
          if(this.Comparer.Compare(elem, left) > 0) {
            this.Array[leftIdx] = elem;
            this.Array[idx] = left;
            this.BubbleDown(leftIdx);
          }
        } else {
          if(this.Comparer.Compare(elem, right) > 0) {
            this.Array[rightIdx] = elem;
            this.Array[idx] = right;
            this.BubbleDown(rightIdx);
          }
        }
      } else if(rightIdx == this.Array.Count) {
        T left = this.Array[leftIdx];
        if(this.Comparer.Compare(elem, left) > 0) {
          this.Array[idx] = left;
          this.Array[leftIdx] = elem;
        }
      }
    }
  }
}
