﻿using NLog;
using SpellEditor.Sources.Binding;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static SpellEditor.Sources.DBC.AbstractDBC;

namespace SpellEditor.Sources.DBC
{
    public class DBCReader
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private string _filePath;
        private long _filePosition;
        private DBCHeader _header;

        public DBCReader(string filePath)
        {
            _filePath = filePath;
        }

        /**
         * Reads a DBC record from a given binary reader.
         */
        private Struct ReadStruct<Struct>(byte[] readBuffer)
        {
            Struct structure;
            GCHandle handle;
            try
            {
                handle = GCHandle.Alloc(readBuffer, GCHandleType.Pinned);
                structure = (Struct)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(Struct));
            }
            catch (Exception e)
            {
                Logger.Info(e);
                throw new Exception(e.Message);
            }
            if (handle != null)
                handle.Free();
            return structure;
        }

        /**
         * Reads the DBC Header, saving it to the class and returning it
         */
        public DBCHeader ReadDBCHeader()
        {
            DBCHeader header;
            using (FileStream fileStream = new FileStream(_filePath, FileMode.Open))
            {
                int count = Marshal.SizeOf(typeof(DBCHeader));
                byte[] readBuffer = new byte[count];
                using (BinaryReader reader = new BinaryReader(fileStream))
                {
                    readBuffer = reader.ReadBytes(count);
                    _filePosition = reader.BaseStream.Position;
                    header = ReadStruct<DBCHeader>(readBuffer);
                }
            }
            _header = header;
            return header;
        }

        /**
         * Reads all the records from the DBC file. It puts each record in a
         * array of key value pairs inside the body. The key value pairs are
         * column name to column value.
         */
        public DBCBody ReadDBCRecords(string bindingName)
        {
            var binding = BindingManager.GetInstance().FindBinding(bindingName);
            if (binding == null)
                throw new Exception($"Binding not found: {bindingName}.txt");
            if (_header.RecordSize != binding.CalcRecordSize())
                throw new Exception($"Binding [{_filePath}] fields size does not match the DBC header record size; expected record size [{binding.CalcRecordSize()}] got [{_header.RecordSize}].");
            if (_header.FieldCount != binding.CalcFieldCount())
                throw new Exception($"Binding [{_filePath}] field count does not match the DBC field count; expected [{binding.CalcFieldCount()}] got [{_header.FieldCount}].");

            var body = new DBCBody
            {
                RecordMaps = new Dictionary<string, object>[_header.RecordCount]
            };
            for (int i = 0; i < _header.RecordCount; ++i)
                body.RecordMaps[i] = new Dictionary<string, object>((int)_header.FieldCount);

            using (FileStream fileStream = new FileStream(_filePath, FileMode.Open))
            {
                using (BinaryReader reader = new BinaryReader(fileStream))
                {
                    reader.BaseStream.Position = _filePosition;
                    for (uint i = 0; i < _header.RecordCount; ++i)
                    {
                        var entry = body.RecordMaps[i];
                        foreach (var field in binding.Fields)
                        {
                            switch (field.Type)
                            {
                                case BindingType.INT:
                                    {
                                        entry.Add(field.Name, reader.ReadInt32());
                                        break;
                                    }
                                case BindingType.STRING_OFFSET:
                                case BindingType.UINT:
                                    {
                                        entry.Add(field.Name, reader.ReadUInt32());
                                        break;
                                    }
                                case BindingType.UINT8:
                                    {
                                        entry.Add(field.Name, reader.ReadByte());
                                        break;
                                    }
                                case BindingType.FLOAT:
                                    {
                                        entry.Add(field.Name, reader.ReadSingle());
                                        break;
                                    }
                                case BindingType.DOUBLE:
                                    {
                                        entry.Add(field.Name, reader.ReadDouble());
                                        break;
                                    }
                                default:
                                    throw new Exception($"Found unkown field type for column {field.Name} type {field.Type} in binding {binding.Name}");
                            }
                        }      
                    }
                    _filePosition = reader.BaseStream.Position;
                }
            }

            return body;
        }

        /**
        * Reads the string block from the DBC file and saves it to the stringsMap
        * The position is saved into the map value so that spell records can
        * reverse lookup strings.
        */
        public Dictionary<uint, VirtualStrTableEntry> ReadStringBlock()
        {
            // Read string block into memory
            string StringBlock;
            using (FileStream fileStream = new FileStream(_filePath, FileMode.Open))
            {
                using (BinaryReader reader = new BinaryReader(fileStream))
                {
                    reader.BaseStream.Position = _filePosition;
                    StringBlock = Encoding.UTF8.GetString(reader.ReadBytes(_header.StringBlockSize));
                }
            }

            // create offsets to string entry lookups
            var stringsMap = new Dictionary<uint, VirtualStrTableEntry>();
           
            string strTokens = "";
            uint strOffset = 0;
            int strBlockOffset = 0;
            int strBlockLength = new StringInfo(StringBlock).LengthInTextElements;
            while (strBlockOffset < strBlockLength)
            {
                var token = StringBlock[strBlockOffset];
                // Read until we hit a string terminator
                if (token == '\0')
                {
                    stringsMap.Add(strOffset, new VirtualStrTableEntry
                    {
                        Value = strTokens,
                        NewValue = 0
                    });

                    // account for the string terminator char (+ 1)
                    strOffset += (uint)Encoding.UTF8.GetByteCount(strTokens) + 1;
                    strTokens = "";
                }
                else
                {
                    strTokens += token;
                }
                ++strBlockOffset;
            }

            return stringsMap;
        }
    }
}
