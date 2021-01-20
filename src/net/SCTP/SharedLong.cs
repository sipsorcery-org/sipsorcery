using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace SIPSorcery.net.SCTP
{
    public class SharedLong
    {
        private object myLock = new object();
        private long _sharedInteger;

        public SharedLong(long defaultValue)
        {
            this._sharedInteger = defaultValue;
        }

        public void Add(int amount)
        {
            lock (myLock)
            {
                _sharedInteger += amount;
            }
        }

        public long AddNoLock(long amount)
        {
            _sharedInteger += amount;
            return _sharedInteger;
        }

        public void Subtract(long amount)
        {
            lock(myLock)
            {
                _sharedInteger -= amount;
            }
        }

        public long SubtractNoLock(long amount)
        {
            _sharedInteger -= amount;
            return _sharedInteger;
        }

        public long GetValue()
        {
            lock (myLock)
            {
                return _sharedInteger;
            }
        }

        public void ExecuteOperation(Action a)
        {
            lock (myLock)
            {
                a.Invoke();
            }
        }

        public void ExecuteOperation(Action<long> a)
        {
            lock (myLock)
            {
                a.Invoke(_sharedInteger);
            }
        }

        public void ExecuteOperation(Func<int> a)
        {
            lock (myLock)
            {
                _sharedInteger = a.Invoke();
            }
        }

        public void ExecuteOperation(Func<long,long> a)
        {
            lock (myLock)
            {
                _sharedInteger = a.Invoke(_sharedInteger);
            }
        }

        public T ExecuteOperation<T>(Func<long, T> a)
        {
            lock (myLock)
            {
                return a.Invoke(_sharedInteger);
            }
        }

        public T ExecuteOperation<T>(Func<T> a)
        {
            lock (myLock)
            {
                return a.Invoke();
            }
        }

        public void SetValueNoLock(long value)
        {
            _sharedInteger = value;
        }

        public void SetValue(long value)
        {
            lock (myLock)
            {
                _sharedInteger = value;
            }
        }

        public void SetValue(Func<long> value)
        {
            lock (myLock)
            {
                _sharedInteger = value();
            }
        }
    }
}
