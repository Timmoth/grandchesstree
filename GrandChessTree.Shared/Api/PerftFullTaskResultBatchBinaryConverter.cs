using System;
using System;
using System.IO;

namespace GrandChessTree.Shared.Api
{
    
    public static class PerftFullTaskResultBatchBinaryConverter
    {
        /// <summary>
        /// Encodes a PerftFullTaskResultBatch into a binary byte array in little‑endian format.
        /// </summary>
        public static byte[] Encode(PerftFullTaskResultBatch batch)
        {
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms))
            {
                // Write header fields.
                WriteInt32LE(writer, batch.WorkerId);
                WriteInt32LE(writer, batch.AllocatedMb);
                WriteInt32LE(writer, batch.Threads);
                WriteSingleLE(writer, batch.Mips);

                // Write the 2D array: first the number of rows.
                WriteInt32LE(writer, batch.Results.Length);
                foreach (var row in batch.Results)
                {
                    // Write the length of each row.
                    WriteInt32LE(writer, row.Length);
                    // Write each ulong value.
                    foreach (ulong value in row)
                    {
                        WriteUInt64LE(writer, value);
                    }
                }
            }
            return ms.ToArray();
        }

        /// <summary>
        /// Decodes a PerftFullTaskResultBatch from a binary byte array assuming little‑endian format.
        /// </summary>
        public static PerftFullTaskResultBatch Decode(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            var workerId = ReadInt32LE(reader);
            var allocatedMb = ReadInt32LE(reader);
            var threads = ReadInt32LE(reader);
            var mips = ReadSingleLE(reader);

            int rowCount = ReadInt32LE(reader);
            var results = new ulong[rowCount][];

            var batch = new PerftFullTaskResultBatch
            {
                WorkerId = workerId,
                AllocatedMb = allocatedMb,
                Threads = threads,
                Mips = mips,
                Results = results
            };

            for (int i = 0; i < rowCount; i++)
            {
                int colCount = ReadInt32LE(reader);
                var row = new ulong[colCount];
                for (int j = 0; j < colCount; j++)
                {
                    row[j] = ReadUInt64LE(reader);
                }
                results[i] = row;
            }
            batch.Results = results;
            return batch;
        }

        // Helper methods to always write in little‑endian order.
        private static void WriteInt32LE(BinaryWriter writer, int value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                var bytes = BitConverter.GetBytes(value);
                Array.Reverse(bytes);
                writer.Write(bytes);
            }
            else
            {
                writer.Write(value);
            }
        }

        private static int ReadInt32LE(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(sizeof(int));
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToInt32(bytes, 0);
        }

        private static void WriteSingleLE(BinaryWriter writer, float value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                var bytes = BitConverter.GetBytes(value);
                Array.Reverse(bytes);
                writer.Write(bytes);
            }
            else
            {
                writer.Write(value);
            }
        }

        private static float ReadSingleLE(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(sizeof(float));
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToSingle(bytes, 0);
        }

        private static void WriteUInt64LE(BinaryWriter writer, ulong value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                var bytes = BitConverter.GetBytes(value);
                Array.Reverse(bytes);
                writer.Write(bytes);
            }
            else
            {
                writer.Write(value);
            }
        }

        private static ulong ReadUInt64LE(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(sizeof(ulong));
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToUInt64(bytes, 0);
        }
    }

}
