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
using System.IO;
using System.Net;
using System.Text;

namespace Tmds.MDns
{
    struct Header
    {
        public ushort TransactionID;
        public ushort QuestionCount;
        public ushort AnswerCount;
        public ushort AuthorityCount;
        public ushort AdditionalCount;
        public ushort Flags;

        public bool IsQuery { get { return (!IsResponse); } }
        public bool IsResponse { get { return ((Flags & (1 << 15)) != 0); } }
        
        public bool IsStandardQuery { get { return ((Flags & (0xf << 9)) == 0); } }
        public bool IsInverseQuery { get { return ((Flags & (0xf << 9)) == 1); } }
        public bool IsServerStatusRequest { get { return ((Flags & (0xf << 9)) == 2); } }
        
        public bool IsAuthorativeAnswer { get { return ((Flags & (1 << 10)) != 0); } }
        public bool IsTruncated { get { return ((Flags & (1 << 9)) != 0); } }
        public bool IsRecursionDesired { get { return ((Flags & (1 << 8)) != 0); } }
        public bool IsRecursionAvailable { get { return ((Flags & (1 << 7)) != 0); } }
        
        public bool IsNoError { get { return ((Flags & 0xf) == 0); } }
        public bool IsFormatError { get { return ((Flags & 0xf) == 1); } }
        public bool IsServerFailure { get { return ((Flags & 0xf) == 2); } }
        public bool IsNameError { get { return ((Flags & 0xf) == 3); } }
        public bool IsNotImplemented { get { return ((Flags & 0xf) == 4); } }
        public bool IsRefused { get { return ((Flags & 0xf) == 5); } }
    }
    struct Question
    {
        public Name QName;
        public RecordType QType;
        public RecordClass QClass;
    }
    struct RecordHeader
    {
        public Name Name;
        public RecordType Type;
        public RecordClass Class;
        public uint Ttl;
        public ushort DataLength;
    }
    struct SrvRecord
    {
        public ushort Priority;
        public ushort Weight;
        public ushort Port;
        public Name Target;
    }
    class DnsMessageReader
    {
        public DnsMessageReader(Stream stream)
        {
            BaseStream = stream;
        }

        public Header ReadHeader()
        {
            ushort transactionId = ReadUInt16();
            ushort flags = ReadUInt16();
            ushort questionCount = ReadUInt16();
            ushort answerCount = ReadUInt16();
            ushort authorityCount = ReadUInt16();
            ushort additionalCount = ReadUInt16();

            return new Header { 
                TransactionID = transactionId,
                Flags = flags,
                QuestionCount = questionCount,
                AnswerCount = answerCount,
                AuthorityCount = authorityCount,
                AdditionalCount = additionalCount
            };
        }

        public Question ReadQuestion()
        {
            Name qname = ReadName();
            ushort qtype = ReadUInt16();
            ushort qclass = ReadUInt16();

            return new Question
            {
                QName = qname,
                QType = (RecordType)qtype,
                QClass = (RecordClass)qclass
            };
        }

        public RecordHeader ReadRecordHeader()
        {
            if (_nextRecord > 0)
            {
                SkipRecordBytes();
            }

            Name name = ReadName();
            ushort type = ReadUInt16();
            ushort _class = ReadUInt16();
            uint ttl = ReadUInt32();
            ushort rdLength = ReadUInt16();
            _recordLength = rdLength;
            _nextRecord = (int)(BaseStream.Position + rdLength);
            
            return new RecordHeader
            {
                Name = name,
                Type = (RecordType)type,
                Class = (RecordClass)_class,
                Ttl = ttl,
                DataLength = rdLength
            };
        }

        public byte[] ReadRecordBytes()
        {
            return ReadBytes(_recordLength);
        }

        public void SkipRecordBytes()
        {
            BaseStream.Seek(_nextRecord, SeekOrigin.Begin);
        }

        public IPAddress ReadARecord()
        {
            return new IPAddress(ReadBytes(_recordLength));
        }

        public List<string> ReadTxtRecord()
        {
            var txts = new List<string>();
            int rdLength = _recordLength;
            
            while (rdLength > 0)
            {
                var txtLength = ReadByte();
                rdLength -= (ushort)(txtLength + 1);
                if (rdLength < 0)
                {
                    throw new Exception();
                }
                var bytes = ReadBytes(txtLength);
                string txt = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
                txts.Add(txt);
            }

            return txts;
        }

        public SrvRecord ReadSrvRecord()
        {
            ushort priority = ReadUInt16();
            ushort weight = ReadUInt16();
            ushort port = ReadUInt16();
            Name target = ReadName();
            
            return new SrvRecord
            {
                Priority = priority,
                Weight = weight,
                Port = port,
                Target = target
            };
        }

        public Name ReadPtrRecord()
        {
            return ReadName();
        }

        public Stream BaseStream { get; private set; }

        private byte ReadByte()
        {
            int i = BaseStream.ReadByte();
            if (i >= 0)
            {
                return (byte)i;
            }
            throw new Exception();
        }

        private byte[] ReadBytes(int length)
        {
            var buffer = new byte[length];
            int offset = 0;
            
            while (length > 0)
            {
                int bytesRead = BaseStream.Read(buffer, offset, length);
                if (bytesRead <= 0)
                {
                    throw new Exception();
                }
                length -= bytesRead;
                offset += bytesRead;
            }
            
            return buffer;
        }

        private UInt16 ReadUInt16()
        {
            byte b0 = ReadByte();
            byte b1 = ReadByte();
            return (UInt16)((b0 << 8) + b1);
        }

        private UInt32 ReadUInt32()
        {
            byte b0 = ReadByte();
            byte b1 = ReadByte();
            byte b2 = ReadByte();
            byte b3 = ReadByte();
            return (UInt32)((b0 << 24) + (b1 << 16) + (b2 << 8) + b3);
        }

        private Name ReadName()
        {
            Name name = new Name();
            ReadName(name);
            return name;
        }

        private void ReadName(Name name)
        {
            while (true)
            {
                byte labelLength = ReadByte();
                if ((labelLength & 0xC0) == 0xC0)
                {
                    int offset = ((labelLength & 0x3F) << 8) + ReadByte();
                    long oldOffset = BaseStream.Position;
                    BaseStream.Seek(offset, SeekOrigin.Begin);
                    ReadName(name);
                    BaseStream.Seek(oldOffset, SeekOrigin.Begin);
                    return;
                }
                else
                {
                    var bytes = ReadBytes(labelLength);
                    string label = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
                    name.AddLabel(label);
                    if (labelLength == 0)
                    {
                        return;
                    }
                }
            }
        }

        private int _recordLength = -1;
        private int _nextRecord;
    }
}
