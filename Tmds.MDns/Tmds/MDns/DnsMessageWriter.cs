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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace Tmds.MDns
{
    class DnsMessageWriter
    {
        public DnsMessageWriter()
        {
            Stream = new MemoryStream(Buffer);
        }

        public void Reset()
        {
            Stream.Seek(0, SeekOrigin.Begin);
            QuestionCount = 0;
            AnswerCount = 0;
            AuthorityCount = 0;
            AdditionalCount = 0;
            RecordStartPosition = 0;
        }

        public void WriteQueryHeader(ushort transactionId)
        {
            Debug.Assert(Stream.Position == 0);
            WriteUInt16(transactionId);
            WriteUInt16(0); // flags
            WriteUInt16(0); // questionCount
            WriteUInt16(0); // answerCount
            WriteUInt16(0); // authorityCount
            WriteUInt16(0); // additionalCount
        }

        public void WriteQuestion(Name name, RecordType qtype, RecordClass qclass = RecordClass.IN)
        {
            WriteName(name);
            WriteUInt16((ushort)qtype);
            WriteUInt16((ushort)qclass);
            QuestionCount++;
        }

        public void WritePtrRecord(RecordSection recordType, Name name, Name ptrName, uint ttl, RecordClass _class = RecordClass.IN)
        {
            WriteRecordStart(recordType, name, RecordType.PTR, ttl, _class);
            WriteRecordData(name);
            WriteRecordEnd();
        }

        public void WriteRecordStart(RecordSection recordType, Name name, RecordType type, uint ttl, RecordClass _class = RecordClass.IN)
        {
            Debug.Assert(RecordStartPosition == 0);
            WriteName(name);
            WriteUInt16((ushort)type);
            WriteUInt16((ushort)_class);
            WriteUInt32(ttl);
            WriteUInt16(0);
            switch (recordType)
            {
                case RecordSection.Answer:
                    AnswerCount++;
                    break;
                case RecordSection.Additional:
                    AdditionalCount++;
                    break;
                case RecordSection.Authority:
                    AuthorityCount++;
                    break;
            }
            RecordStartPosition = Stream.Position;
        }

        public void WriteRecordData(byte[] buffer, int offset, int length)
        {
            Debug.Assert(RecordStartPosition != 0);
            Stream.Write(buffer, offset, length);
        }

        public void WriteRecordData(byte[] buffer)
        {
            Stream.Write(buffer, 0, buffer.Length);
        }

        public void WriteRecordData(Name name)
        {
            Debug.Assert(RecordStartPosition != 0);
            WriteName(name);
        }

        public void WriteRecordData(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();
            WriteRecordData(bytes);
        }

        public void WriteRecordEnd()
        {
            Debug.Assert(RecordStartPosition != 0);
            long currentPosition = Stream.Position;
            ushort length = (ushort)(currentPosition - RecordStartPosition);
            Stream.Seek(RecordStartPosition - 2, SeekOrigin.Begin);
            WriteUInt16(length);
            Stream.Seek(currentPosition, SeekOrigin.Begin);
            RecordStartPosition = 0;
        }

        public IList<ArraySegment<byte>> Packets
        {
            get
            {
                finish();
                return new List<ArraySegment<byte>>()
                {
                    new ArraySegment<byte>(Buffer, 0, (int)Stream.Position)
                };
            }
        }

        private void WriteUInt16(ushort value)
        {
            Stream.WriteByte((byte)(value >> 8));
            Stream.WriteByte((byte)(value & 0xff));
        }

        private void WriteUInt32(uint value)
        {
            Stream.WriteByte((byte)((value & 0xff000000) >> 24));
            Stream.WriteByte((byte)((value & 0x00ff0000) >> 16));
            Stream.WriteByte((byte)((value & 0x0000ff00) >> 8));
            Stream.WriteByte((byte)((value & 0x000000ff) >> 0));
        }

        private void WriteName(Name name)
        {
            bool finished = false;
            foreach(string label in name.Labels)
            {
                int length = label.Length;
                finished = (length == 0);
                Stream.WriteByte((byte)length);
                Encoding.UTF8.GetBytes(label, 0, label.Length, Buffer, (int)Stream.Position);
                Stream.Seek(length, SeekOrigin.Current);
            }
            if (!finished)
            {
                Stream.WriteByte(0);
            }
        }

        private void finish()
        {
            long currentPosition = Stream.Position;
            Stream.Seek(4, SeekOrigin.Begin);
            WriteUInt16(QuestionCount);
            WriteUInt16(AnswerCount);
            WriteUInt16(AuthorityCount);
            WriteUInt16(AdditionalCount);
            Stream.Seek(currentPosition, SeekOrigin.Begin);
        }

        private byte[] Buffer = new byte[9000];
        private MemoryStream Stream;
        private ushort QuestionCount;
        private ushort AnswerCount;
        private ushort AuthorityCount;
        private ushort AdditionalCount;
        private long RecordStartPosition;
    }
}
