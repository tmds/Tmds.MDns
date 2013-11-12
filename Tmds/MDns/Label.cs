//Copyright (C) 2013  Tom Deseyn

//This library is free software; you can redistribute it and/or
//modify it under the terms of the GNU Lesser General Public
//License as published by the Free Software Foundation; either
//version 2.1 of the License, or (at your option) any later version.

//This library is distributed in the hope that it will be useful,
//but WITHOUT ANY WARRANTY; without even the implied warranty of
//MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
//Lesser General Public License for more details.

//You should have received a copy of the GNU Lesser General Public
//License along with this library; if not, write to the Free Software
//Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA

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
            _labels = name.Split(new char[] { '.' }).ToList();
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
            Name name = new Name();
            for (int i = startIndex; i < _labels.Count; i++)
            {
                name.AddLabel(_labels[i]);
            }
            return name;
        }

        public Name SubName(int startIndex, int length)
        {
            Name name = new Name();
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
                StringBuilder sb = new StringBuilder(255);
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
            return StringComparer.InvariantCultureIgnoreCase.Compare(ToString(), name.ToString());
        }

        public override bool Equals(object obj)
        {
            return StringComparer.InvariantCultureIgnoreCase.Equals(ToString(), obj.ToString());
        }

        public override int GetHashCode()
        {
            return StringComparer.InvariantCultureIgnoreCase.GetHashCode(ToString());
        }

        public IList<string> Labels
        {
            get { return _labels.AsReadOnly(); }
        }

        private List<string> _labels = new List<string>();
        private string _name;
    }
}
