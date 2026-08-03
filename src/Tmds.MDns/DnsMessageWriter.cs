// This file is part of Tmds.MDns which is released under MIT.
// See file LICENSE for full license details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace Tmds.MDns
{
    class DnsMessageWriter
    {
        public DnsMessageWriter()
        {
            _stream = new MemoryStream(_buffer);
        }

        public void Reset()
        {
            _stream.Seek(0, SeekOrigin.Begin);
            _questionCount = 0;
            _answerCount = 0;
            _authorityCount = 0;
            _additionalCount = 0;
            _recordStartPosition = 0;
        }

        public void WriteQueryHeader(ushort transactionId)
        {
            Debug.Assert(_stream.Position == 0);
            WriteUInt16(transactionId);
            WriteUInt16(0); // flags
            WriteUInt16(0); // questionCount
            WriteUInt16(0); // answerCount
            WriteUInt16(0); // authorityCount
            WriteUInt16(0); // additionalCount
        }

        public void WriteQuestion(Name name, RecordType qtype, RecordClass qclass = RecordClass.Internet)
        {
            WriteName(name);
            WriteUInt16((ushort)qtype);
            WriteUInt16((ushort)qclass);
            _questionCount++;
        }

        public void WritePtrRecord(RecordSection recordType, Name name, Name ptrName, uint ttl, RecordClass _class = RecordClass.Internet)
        {
            WriteRecordStart(recordType, name, RecordType.PTR, ttl, _class);
            WriteRecordData(ptrName);
            WriteRecordEnd();
        }

        public void WriteRecordStart(RecordSection recordType, Name name, RecordType type, uint ttl, RecordClass _class = RecordClass.Internet)
        {
            Debug.Assert(_recordStartPosition == 0);
            WriteName(name);
            WriteUInt16((ushort)type);
            WriteUInt16((ushort)_class);
            WriteUInt32(ttl);
            WriteUInt16(0);
            switch (recordType)
            {
                case RecordSection.Answer:
                    _answerCount++;
                    break;
                case RecordSection.Additional:
                    _additionalCount++;
                    break;
                case RecordSection.Authority:
                    _authorityCount++;
                    break;
            }
            _recordStartPosition = _stream.Position;
        }

        public void WriteRecordData(byte[] buffer, int offset, int length)
        {
            Debug.Assert(_recordStartPosition != 0);
            _stream.Write(buffer, offset, length);
        }

        public void WriteRecordData(byte[] buffer)
        {
            _stream.Write(buffer, 0, buffer.Length);
        }

        public void WriteRecordData(Name name)
        {
            Debug.Assert(_recordStartPosition != 0);
            WriteName(name);
        }

        public void WriteRecordData(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();
            WriteRecordData(bytes);
        }

        public void WriteRecordEnd()
        {
            long currentPosition = _stream.Position;
            var length = (ushort)(currentPosition - _recordStartPosition);
            _stream.Seek(_recordStartPosition - 2, SeekOrigin.Begin);
            WriteUInt16(length);
            _stream.Seek(currentPosition, SeekOrigin.Begin);
            _recordStartPosition = 0;
        }

        public IList<ArraySegment<byte>> Packets
        {
            get
            {
                Finish();
                return new List<ArraySegment<byte>>
                {
                    new ArraySegment<byte>(_buffer, 0, (int)_stream.Position)
                };
            }
        }

        private void WriteUInt16(ushort value)
        {
            _stream.WriteByte((byte)(value >> 8));
            _stream.WriteByte((byte)(value & 0xff));
        }

        private void WriteUInt32(uint value)
        {
            _stream.WriteByte((byte)((value & 0xff000000) >> 24));
            _stream.WriteByte((byte)((value & 0x00ff0000) >> 16));
            _stream.WriteByte((byte)((value & 0x0000ff00) >> 8));
            _stream.WriteByte((byte)((value & 0x000000ff) >> 0));
        }

        private void WriteName(Name name)
        {
            bool finished = false;
            foreach(string label in name.Labels)
            {
                int length = Encoding.UTF8.GetByteCount(label);
                finished = (length == 0);
                _stream.WriteByte((byte)length);
                Encoding.UTF8.GetBytes(label, 0, label.Length, _buffer, (int)_stream.Position);
                _stream.Seek(length, SeekOrigin.Current);
            }
            if (!finished)
            {
                _stream.WriteByte(0);
            }
        }

        private void Finish()
        {
            long currentPosition = _stream.Position;
            _stream.Seek(4, SeekOrigin.Begin);
            WriteUInt16(_questionCount);
            WriteUInt16(_answerCount);
            WriteUInt16(_authorityCount);
            WriteUInt16(_additionalCount);
            _stream.Seek(currentPosition, SeekOrigin.Begin);
        }

        private readonly byte[] _buffer = new byte[9000];
        private readonly MemoryStream _stream;
        private ushort _questionCount;
        private ushort _answerCount;
        private ushort _authorityCount;
        private ushort _additionalCount;
        private long _recordStartPosition;
    }
}
