using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GrandChessTree.Shared.Api
{
    public static class PerftTasksBinaryConverter
    {
        /// <summary>
        /// Encodes a list of PerftFastTaskResponse into a binary format.
        /// </summary>
        public static byte[] Encode(List<PerftTaskResponse> responses)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

            // Write the number of responses
            WriteInt32LE(writer, responses.Count);

            foreach (var response in responses)
            {
                WriteInt64LE(writer, response.TaskId);
                WriteStringLE(writer, response.Board);
                WriteInt32LE(writer, response.Depth);
                WriteInt32LE(writer, response.LaunchDepth);
            }

            return ms.ToArray();
        }

        /// <summary>
        /// Decodes a binary byte array into a list of PerftFastTaskResponse.
        /// </summary>
        public static List<PerftTaskResponse> Decode(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            int count = ReadInt32LE(reader);
            var responses = new List<PerftTaskResponse>(count);

            for (int i = 0; i < count; i++)
            {
                var response = new PerftTaskResponse
                {
                    TaskId = ReadInt64LE(reader),
                    Board = ReadStringLE(reader),
                    Depth = ReadInt32LE(reader),
                    LaunchDepth = ReadInt32LE(reader)
                };
                responses.Add(response);
            }

            return responses;
        }

        // ---- HELPER METHODS ----

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
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }

        private static void WriteInt64LE(BinaryWriter writer, long value)
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

        private static long ReadInt64LE(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(sizeof(long));
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToInt64(bytes, 0);
        }

        private static void WriteStringLE(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteInt32LE(writer, bytes.Length); // Store string length
            writer.Write(bytes);
        }

        private static string ReadStringLE(BinaryReader reader)
        {
            int length = ReadInt32LE(reader);
            byte[] bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }
    }

}
