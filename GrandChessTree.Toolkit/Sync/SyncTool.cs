using System.Diagnostics;
using System.Text.RegularExpressions;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Helpers;

namespace GrandChessTree.Toolkit.Sync
{
    public class SyncTool
    {
        private const int BatchSize = 500_000;
        private const int MaxParallelism = 24;

        public void GenerateRootBatches()
        {
            var sw = Stopwatch.StartNew();

            var (initialBoard, whiteToMove) = FenParser.Parse(Constants.StartPosFen);
            var desiredDepth = 9;
            var launchDepth = 7;
            var searchDepth = desiredDepth - launchDepth;

            UniqueLeafNodeGeneratorCompressed.Occurrences = 1;
            UniqueLeafNodeGeneratorCompressed.PerftRootCompressedUniqueLeafNodes(ref initialBoard, launchDepth, whiteToMove);
            var boards = UniqueLeafNodeGeneratorCompressed.boards.Values.OrderBy(v => v.order).ToList();
            UniqueLeafNodeGeneratorCompressed.boards.Clear();

            var batchFiles = new List<string>();
            var batchIndex = 0;
            for (int i = 0; i < boards.Count; i += BatchSize)
            {
                var batch = boards.GetRange(i, Math.Min(BatchSize, boards.Count - i));
                foreach(var item in batch)
                {
                    var (board, wtm) = BoardStateSerialization.FromByteArray(item.board);
                    UniqueLeafNodeGeneratorCompressed.Occurrences = item.occurrences;
                    UniqueLeafNodeGeneratorCompressed.PerftRootCompressedUniqueLeafNodes(ref board, searchDepth, wtm);
                }

                Console.WriteLine($"Generated batch {i}");

                var batchFilename = $"temp_batch_{batchIndex}.bin";
                WriteBatchToBinaryFile(UniqueLeafNodeGeneratorCompressed.boards.Count, UniqueLeafNodeGeneratorCompressed.boards.Select(v => new BoardRecord
                {
                    Board = v.Value.board,
                    Hash = v.Key,
                    Occurrences = v.Value.occurrences,
                    Order = v.Value.order,
                }), batchFilename);
                batchFiles.Add(batchFilename);

                Console.WriteLine($"{UniqueLeafNodeGeneratorCompressed.boards.Values.Count} tasks inserted");
                UniqueLeafNodeGeneratorCompressed.boards.Clear();
                batchIndex++;
            }

            var ms = sw.ElapsedMilliseconds;
            Console.WriteLine("----- RESULTS -----");
            Console.WriteLine($"Unique positions: {(ulong)PerftUnique.UniquePositions.Count}");
            Console.WriteLine($"Time: {ms}ms");
            Console.WriteLine("-------------------");
        }
        public static List<string> GetBatchFileNames(string directory)
        {
            string pattern = @"temp_batch_(\d+)\.bin";
            return Directory.GetFiles(directory, "temp_batch_*.bin")
                .Select(path => new { Path = path, Match = Regex.Match(Path.GetFileName(path), pattern) })
                .Where(x => x.Match.Success)
                .OrderBy(x => int.Parse(x.Match.Groups[1].Value))
                .Select(x => x.Path)
                .ToList();
        }

        public static List<string> GetSortedBatchFileNames(string directory)
        {
            string pattern = @"sorted_output_(\d+)\.bin";
            return Directory.GetFiles(directory, "sorted_output_*.bin")
                .Select(path => new { Path = path, Match = Regex.Match(Path.GetFileName(path), pattern) })
                .Where(x => x.Match.Success)
                .OrderBy(x => int.Parse(x.Match.Groups[1].Value))
                .Select(x => x.Path)
                .ToList();
        }
        public void MergeBatches()
        {
            var mergeFile = "merged_output.bin";
            var unsortedFiles = GetBatchFileNames("./");
            var mergedFilePath = ExternalSorter.ExternalMergeSort(
                   GetBatchFileNames("./"),
                    mergeFile
                );
        }

        public void SortAndBatch()
        {
            var mergeFile = "merged_output.bin";
            var sortedFile = "sorted_output";
            ExternalSorter.SortMergedFileByOrder(mergeFile, sortedFile, 20_000_000, 10_000_000);
        }

        public static void WriteBatchToBinaryFile(int count, IEnumerable<BoardRecord> batchRecords, string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(fs);

            // Write record count at beginning.
            writer.Write(count);
            foreach (var record in batchRecords.OrderBy(r => r.Hash))
            {
                writer.Write(record.Hash);
                writer.Write(record.Order);
                writer.Write(record.Occurrences);
                writer.Write(record.Board);
            }
        }

        public void ProcessFile()
        {
            ulong uniqueCount = 0;
            ulong totalCount = 0;

            var batchFiles = GetSortedBatchFileNames("./").Take(1);

            foreach (var batchFile in batchFiles)
            {// Read all records from the input file.
                using (var fs = new FileStream(batchFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(fs))
                {
                    // Read the total number of records.
                    int count = reader.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        var occurrences = reader.ReadInt32();
                        var board = reader.ReadBytes(26);

                        uniqueCount++;
                        totalCount += (ulong)occurrences;
                    }
                }
            }

            

            Console.WriteLine($"unique: {uniqueCount}");
            Console.WriteLine($"total: {totalCount}");
        }

    }
    public struct BoardRecord
    {
        public ulong Hash;       // 8 bytes
        public ulong Order;      // 4 bytes
        public int Occurrences;  // 4 bytes
        public byte[] Board;     // 26 bytes
    }

    public struct BoardRecord2
    {
        public ulong Order;      // 4 bytes
        public int Occurrences;  // 4 bytes
        public byte[] Board;     // 26 bytes
    }

    public struct BoardRecord3
    {
        public int Occurrences;  // 4 bytes
        public byte[] Board;     // 26 bytes
    }

    public class MergedRecord
    {
        public BoardRecord Record;
        public RecordReader RecordReader;
    }

    public class MergedRecord2
    {
        public BoardRecord2 Record;
        public RecordReader2 RecordReader;
    }

    public class RecordReader : IDisposable
    {
        public BinaryReader Reader { get; }
        public int RecordsRemaining { get; private set; }

        public RecordReader(string filePath)
        {
            var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Reader = new BinaryReader(fs);
            RecordsRemaining = Reader.ReadInt32(); // read the record count written at the start.
        }

        /// <summary>
        /// Reads the next BoardRecord from the stream, or returns null if done.
        /// </summary>
        public BoardRecord? ReadNextRecord()
        {
            if (RecordsRemaining <= 0)
                return null;
            try
            {
                ulong hash = Reader.ReadUInt64();
                ulong order = Reader.ReadUInt64();
                int occurrences = Reader.ReadInt32();
                byte[] board = Reader.ReadBytes(26);
                RecordsRemaining--;
                return new BoardRecord
                {
                    Hash = hash,
                    Order = order,
                    Occurrences = occurrences,
                    Board = board
                };
            }
            catch (EndOfStreamException)
            {
                return null;
            }
        }

        public void Dispose() => Reader.Dispose();
    }

    public class RecordReader2 : IDisposable
    {
        public BinaryReader Reader { get; }
        public int RecordsRemaining { get; private set; }

        public RecordReader2(string filePath)
        {
            var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Reader = new BinaryReader(fs);
            RecordsRemaining = Reader.ReadInt32(); // read the record count written at the start.
        }

        /// <summary>
        /// Reads the next BoardRecord from the stream, or returns null if done.
        /// </summary>
        public BoardRecord2? ReadNextRecord()
        {
            if (RecordsRemaining <= 0)
                return null;
            try
            {
                ulong order = Reader.ReadUInt64();
                int occurrences = Reader.ReadInt32();
                byte[] board = Reader.ReadBytes(26);
                RecordsRemaining--;
                return new BoardRecord2
                {
                    Order = order,
                    Occurrences = occurrences,
                    Board = board
                };
            }
            catch (EndOfStreamException)
            {
                return null;
            }
        }

        public void Dispose() => Reader.Dispose();
    }


    public static class ExternalSorter
    {
        public static List<string> SortMergedFileByOrder(
         string inputFile,
         string outputFilePrefix,
         int maxRecordsPerChunk,
         int maxRecordsPerOutputFile)
        {
            // Phase 1: Partition the input file into sorted temporary chunks.
            List<string> tempFiles = new List<string>();

            using (var fs = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                // Read total record count (we don't need it here).
                int totalRecords = reader.ReadInt32();
                while (fs.Position < fs.Length)
                {
                    List<BoardRecord2> chunk = new List<BoardRecord2>();
                    // Read up to maxRecordsPerChunk records.
                    for (int i = 0; i < maxRecordsPerChunk && fs.Position < fs.Length; i++)
                    {
                        BoardRecord2 record = new BoardRecord2
                        {
                            Order = reader.ReadUInt64(),
                            Occurrences = reader.ReadInt32(),
                            Board = reader.ReadBytes(26)
                        };
                        chunk.Add(record);
                    }

                    // Sort this chunk by Order.
                    var sortedChunk = chunk.OrderBy(r => r.Order).ToList();
                    // Write the sorted chunk to a temporary file.
                    string tempFile = Path.GetTempFileName();
                    using (var tempFs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                    using (var writer = new BinaryWriter(tempFs))
                    {
                        writer.Write(sortedChunk.Count);
                        foreach (var rec in sortedChunk)
                        {
                            writer.Write(rec.Order);
                            writer.Write(rec.Occurrences);
                            writer.Write(rec.Board);
                        }
                    }
                    tempFiles.Add(tempFile);
                }
            }

            // Phase 2: Merge the sorted chunks using a priority queue keyed on Order.
            // (Requires .NET 6 or later for PriorityQueue<TElement,TPriority>)
            var pq = new PriorityQueue<MergedRecord2, ulong>();
            List<RecordReader2> readers = new List<RecordReader2>();
            foreach (var file in tempFiles)
            {
                var rr = new RecordReader2(file);
                readers.Add(rr);
                var rec = rr.ReadNextRecord();
                if (rec.HasValue)
                {
                    pq.Enqueue(new MergedRecord2 { Record = rec.Value, RecordReader = rr }, rec.Value.Order);
                }
            }

            List<string> outputFiles = new List<string>();
            int outputFileIndex = 1;
            int currentOutputCount = 0;
            // Create first output file.
            string currentOutputFile = $"{outputFilePrefix}_{outputFileIndex}.bin";
            var outFs = new FileStream(currentOutputFile, FileMode.Create, FileAccess.Write);
            var writerOut = new BinaryWriter(outFs);
            // Write a placeholder for the record count.
            writerOut.Write(0);

            while (pq.Count > 0)
            {
                var smallest = pq.Dequeue();
                var rec = smallest.Record;
                writerOut.Write(rec.Occurrences);
                writerOut.Write(rec.Board);
                currentOutputCount++;

                // If reached the max per output file, finish current file and start a new one.
                if (currentOutputCount >= maxRecordsPerOutputFile)
                {
                    // Go back and update the record count at the start.
                    outFs.Seek(0, SeekOrigin.Begin);
                    writerOut.Write(currentOutputCount);
                    writerOut.Close();
                    outFs.Close();
                    outputFiles.Add(currentOutputFile);

                    // Reset counter and create next output file.
                    outputFileIndex++;
                    currentOutputFile = $"{outputFilePrefix}_{outputFileIndex}.bin";
                    outFs = new FileStream(currentOutputFile, FileMode.Create, FileAccess.Write);
                    writerOut = new BinaryWriter(outFs);
                    writerOut.Write(0); // placeholder
                    currentOutputCount = 0;
                }

                // Enqueue the next record from the same reader.
                var nextRec = smallest.RecordReader.ReadNextRecord();
                if (nextRec.HasValue)
                {
                    pq.Enqueue(new MergedRecord2 { Record = nextRec.Value, RecordReader = smallest.RecordReader }, nextRec.Value.Order);
                }
            }

            // Finalize the last output file if it contains any records.
            if (currentOutputCount > 0)
            {
                outFs.Seek(0, SeekOrigin.Begin);
                writerOut.Write(currentOutputCount);
                writerOut.Close();
                outFs.Close();
                outputFiles.Add(currentOutputFile);
            }
            else
            {
                // If no records were written to the last file, close and delete it.
                writerOut.Close();
                outFs.Close();
                File.Delete(currentOutputFile);
            }

            // Cleanup: Dispose all record readers and delete temporary files.
            foreach (var rr in readers)
            {
                rr.Dispose();
            }
            foreach (var file in tempFiles)
            {
                File.Delete(file);
            }

            return outputFiles;
        }
        public static string ExternalMergeSort(List<string> inputFiles, string outputFile)
        {
            // Create a RecordReader for each input file.
            var recordReaders = inputFiles.Select(file => new RecordReader(file)).ToList();

            // PriorityQueue with key = BoardRecord.Hash.
            var pq = new PriorityQueue<MergedRecord, ulong>();

            // Load the first record from each file.
            foreach (var rr in recordReaders)
            {
                var rec = rr.ReadNextRecord();
                if (rec.HasValue)
                {
                    pq.Enqueue(new MergedRecord { Record = rec.Value, RecordReader = rr }, rec.Value.Hash);
                }
            }

            using var outputFs = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(outputFs);

            // We'll write a placeholder for record count and update it at the end.
            writer.Write(0); // placeholder
            ulong outputRecordCount = 0;
            BoardRecord? lastRecord = null;

            // Process records from the priority queue.
            while (pq.Count > 0)
            {
                var smallest = pq.Dequeue();
                var currentRecord = smallest.Record;

                // If the last record has the same hash, aggregate the occurrences.
                if (lastRecord.HasValue && lastRecord.Value.Hash == currentRecord.Hash)
                {
                    var temp = lastRecord.Value;
                    temp.Occurrences += currentRecord.Occurrences;
                    lastRecord = temp;
                }
                else
                {
                    // Write the previous record if it exists.
                    if (lastRecord.HasValue)
                    {
                        WriteRecord2(writer, lastRecord.Value);
                        outputRecordCount++;

                        if(outputRecordCount%1_000_000 == 0)
                        {
                            Console.WriteLine($"output: {outputRecordCount.FormatBigNumber()} tasks");
                        }
                    }
                    lastRecord = currentRecord;
                }

                // Read next record from the same file and enqueue it if present.
                var nextRecord = smallest.RecordReader.ReadNextRecord();
                if (nextRecord.HasValue)
                {
                    pq.Enqueue(new MergedRecord { Record = nextRecord.Value, RecordReader = smallest.RecordReader }, nextRecord.Value.Hash);
                }
            }
            // Write the final record.
            if (lastRecord.HasValue)
            {
                WriteRecord2(writer, lastRecord.Value);
                outputRecordCount++;
            }

            // Go back and write the correct record count at the beginning.
            writer.Seek(0, SeekOrigin.Begin);
            writer.Write(outputRecordCount);

            // Dispose the record readers.
            foreach (var rr in recordReaders)
            {
                rr.Dispose();
            }

            Console.WriteLine($"finished: {outputRecordCount.FormatBigNumber()} tasks");

            return outputFile;
        }

        private static void WriteRecord(BinaryWriter writer, BoardRecord record)
        {
            writer.Write(record.Hash);
            writer.Write(record.Order);
            writer.Write(record.Occurrences);
            writer.Write(record.Board);
        }
        private static void WriteRecord2(BinaryWriter writer, BoardRecord record)
        {
            writer.Write(record.Order);
            writer.Write(record.Occurrences);
            writer.Write(record.Board);
        }
        private static void WriteRecord2(BinaryWriter writer, BoardRecord2 record)
        {
            writer.Write(record.Order);
            writer.Write(record.Occurrences);
            writer.Write(record.Board);
        }

        private static void WriteRecord3(BinaryWriter writer, BoardRecord3 record)
        {
            writer.Write(record.Occurrences);
            writer.Write(record.Board);
        }
    }
}
