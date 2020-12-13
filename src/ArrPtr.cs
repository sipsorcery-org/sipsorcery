//-----------------------------------------------------------------------------
// Filename: ArrPtr.cs
//
// Description: This class can be used where the common C pattern of using
// a pointer to an array is employed. For example:
//
// MODE_INFO *mip; /* Base of allocated array */
// MODE_INFO* mi;  /* Corresponds to upper left visible macroblock */
//
// The mip variable is either an actual array or as in the case above a pointer
// to a dynamically allocated of items, e.g.:
//
// mip = calloc(100, sizeof(MODE_INFO));
//
// The mi pointer is then typically set to a location within mip, e.g.:
//
// mi = mip + 10;
//
// The same approach can be used in C# IF the array is pinned with "fixed". 
// If the array and pointer are properties of a class and passed as parameters
// keeping track of the pins becomes unweildy.
//
// This class sovles that problem by replacing the mi pointer with an index into
// the mip managed array. Any pointer arithmetic that would have been carried out
// on mi gets applied to the index instead.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 03 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace Vpx.Net
{
    public struct ArrPtr<T>
    {
        private T[] _src { get; }
        public int Index { get; private set; }

        public  ArrPtr(T[] src, int index = 0)
        {
            _src = src;
            Index = index;
        }

        public ref T get()
        {
            return ref _src[Index];
        }

        public T get(int i)
        {
            return _src[Index + i];
        }

        public void set(T val)
        {
            _src[Index] = val;
        }

        public void set(int index, T val)
        {
            _src[Index + index] = val;
        }

        public void setMultiple(T val, int count)
        {
            for (int i = 0; i < count; i++)
            {
                _src[Index + i] = val;
            }
        }

        public T[] src()
        {
            return _src;
        }

        public static ArrPtr<T> operator ++(ArrPtr<T> x)
        {
            x.Index++;
            return x;
        }

        public static ArrPtr<T> operator --(ArrPtr<T> x)
        {
            x.Index--;
            return x;
        }

        public static ArrPtr<T> operator +(ArrPtr<T> x, int i)
        {
            x.Index += i;
            return x;
        }

        public static ArrPtr<T> operator -(ArrPtr<T> x, int i)
        {
            x.Index -= i;
            return x;
        }
    }
}
