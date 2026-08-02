// This file is part of Tmds.MDns which is released under MIT.
// See file LICENSE for full license details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Tmds.MDns
{
    class ReadOnlyCollectionWrapper<T> : ICollection<T>
    {
        public ReadOnlyCollectionWrapper(ICollection<T> collection)
        {
            _baseCollection = collection;
        }

        void ICollection<T>.Add(T item)
        {
            throw new NotSupportedException();
        }

        void ICollection<T>.Clear()
        {
            throw new NotSupportedException();
        }

        public bool Contains(T item)
        {
            return _baseCollection.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _baseCollection.CopyTo(array, arrayIndex);
        }

        public int Count
        {
            get { return _baseCollection.Count; }
        }

        public bool IsReadOnly
        {
            get { return true; }
        }

        bool ICollection<T>.Remove(T item)
        {
            throw new NotSupportedException();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _baseCollection.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return (_baseCollection as IEnumerable).GetEnumerator();
        }

        ICollection<T> _baseCollection;
    }
}
