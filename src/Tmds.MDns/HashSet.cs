using System;
using System.Collections;
using System.Collections.Generic;

namespace Tmds.MDns
{
#if NET20
    class HashSet<T> : ICollection<T>
    {
        private readonly Dictionary<T, int> _dictionary;

        public HashSet()
        {
            _dictionary = new Dictionary<T, int>();
        }

        public HashSet(IEnumerable<T> items) : this()
        {
            if (items == null)
            {
                return;
            }

            foreach (T item in items)
            {
                Add(item);
            }
        }

        public void Add(T item)
        {
            if (null == item)
            {
                throw new ArgumentNullException(nameof(item));
            }

            _dictionary[item] = 0;
        }

        public void Clear()
        {
            _dictionary.Clear();
        }

        public bool Contains(T item)
        {
            return _dictionary.ContainsKey(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (arrayIndex < 0 || arrayIndex >= array.Length || arrayIndex >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            }

            _dictionary.Keys.CopyTo(array, arrayIndex);
        }

        public bool Remove(T item)
        {
            return _dictionary.Remove(item);
        }

        public int Count
        {
            get { return _dictionary.Count; }
        }

        public bool IsReadOnly
        {
            get
            {
                return false;
            }
        }
        
        public IEnumerator<T> GetEnumerator()
        {
            return _dictionary.Keys.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
#endif
}
