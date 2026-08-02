// This file is part of Tmds.MDns which is released under MIT.
// See file LICENSE for full license details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Tmds.MDns
{
    class Name : IComparable<Name>
    {
        public Name(string name)
        {
            _labels = name.Split(new[] { '.' }).ToList();
            _name = name;
        }

        public Name()
        {}

        public void AddLabel(string label)
        {
            _labels.Add(label);
            _name = null;
        }

        public Name SubName(int startIndex)
        {
            var name = new Name();
            for (int i = startIndex; i < _labels.Count; i++)
            {
                name.AddLabel(_labels[i]);
            }
            return name;
        }

        public Name SubName(int startIndex, int length)
        {
            var name = new Name();
            for (int i = startIndex; i < (startIndex + length); i++)
            {
                name.AddLabel(_labels[i]);
            }
            return name;
        }

        public override string ToString()
        {
            if (_name == null)
            {
                var sb = new StringBuilder(255);
                for (int i = 0; i < _labels.Count; i++)
                {
                    if (i != 0)
                    {
                        sb.Append(".");
                    }
                    sb.Append(_labels[i]);
                }
                _name = sb.ToString();
            }
            return _name;
        }

        public int CompareTo(Name name)
        {
            return StringComparer.OrdinalIgnoreCase.Compare(ToString(), name.ToString());
        }

        public override bool Equals(object obj)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(ToString(), obj.ToString());
        }

        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(ToString());
        }

        public IList<string> Labels
        {
            get { return _labels.AsReadOnly(); }
        }

        private readonly List<string> _labels = new List<string>();
        private string _name;
    }
}
