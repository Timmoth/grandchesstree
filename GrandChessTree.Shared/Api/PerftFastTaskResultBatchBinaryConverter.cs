using System;
using System;
using System.IO;

namespace GrandChessTree.Shared.Api
{

    public static class PerftFastTaskResultBatchBinaryConverter
    {
        /// <summary>
        /// Encodes a PerftFastTaskResultBatch into a binary byte array in little‑endian format.
        /// </summary>
        public static byte[] Encode(PerftFastTaskResultBatch batch)
        {
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms))
            {
                // Write header fields
                WriteInt32LE(writer, batch.WorkerId);
                WriteInt32LE(writer, batch.AllocatedMb);
                WriteInt32LE(writer, batch.Threads);
                WriteSingleLE(writer, batch.Mips);

                // Write array length
                WriteInt32LE(writer, batch.Results.Length);

                // Write each ulong value
                foreach (ulong value in batch.Results)
                {
                    WriteUInt64LE(writer, value);
                }
            }
            return ms.ToArray();
        }

        /// <summary>
        /// Decodes a PerftFastTaskResultBatch from a binary byte array assuming little‑endian format.
        /// </summary>
        public static PerftFastTaskResultBatch Decode(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            var workerId = ReadInt32LE(reader);
            var allocatedMb = ReadInt32LE(reader);
            var threads = ReadInt32LE(reader);
            var mips = ReadSingleLE(reader);

            // Read array length
            int arrayLength = ReadInt32LE(reader);
            var results = new ulong[arrayLength];
            var batch = new PerftFastTaskResultBatch
            {
                WorkerId = workerId,
                AllocatedMb = allocatedMb,
                Threads = threads,
                Mips = mips,
                Results = results
            };

            // Read each ulong value
            for (int i = 0; i < arrayLength; i++)
            {
                results[i] = ReadUInt64LE(reader);
            }

            batch.Results = results;
            return batch;
        }

        // Helper methods to always write in little‑endian order.
        private static void WriteInt32LE(BinaryWriter writer, int value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            writer.Write(bytes);
        }

        private static int ReadInt32LE(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(sizeof(int));
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }

        private static void WriteSingleLE(BinaryWriter writer, float value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            writer.Write(bytes);
        }

        private static float ReadSingleLE(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(sizeof(float));
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToSingle(bytes, 0);
        }

        private static void WriteUInt64LE(BinaryWriter writer, ulong value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            writer.Write(bytes);
        }

        private static ulong ReadUInt64LE(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(sizeof(ulong));
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToUInt64(bytes, 0);
        }
    }
}
