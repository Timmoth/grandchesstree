import React, { useEffect, useState } from "react";
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  Tooltip,
  ResponsiveContainer,
  CartesianGrid,
  Legend,
} from "recharts";

// Raw data as returned by the API endpoints
interface RawDataPoint {
  timestamp: number;
  nps: number;
}

// Merged data for charting
interface DataPoint {
  timestamp: number;
  nps_stats_task: number;
  nps_nodes_task: number;
}

const formatBigNumber = (num: number): string => {
  if (num >= 1e12) return (num / 1e12).toFixed(1) + "t";
  if (num >= 1e9) return (num / 1e9).toFixed(1) + "b";
  if (num >= 1e6) return (num / 1e6).toFixed(1) + "m";
  if (num >= 1e3) return (num / 1e3).toFixed(1) + "k";
  return num.toString();
};

const formatTime = (timestamp: number): string => {
  const date = new Date(timestamp * 1000);
  return date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
};

// Helper: bucket raw timestamp (in seconds) to the nearest 15-minute interval (900 seconds)
const getBucket = (timestamp: number): number => {
  return Math.round(timestamp / 900) * 900;
};

interface AccountPerformanceChartProps {
  accountId: number,
}
const AccountPerformanceChart: React.FC<AccountPerformanceChartProps> = ({accountId}) => {
  const [data, setData] = useState<DataPoint[]>([]);

  useEffect(() => {
    const fetchData = async () => {
      try {
        const [statsResponse, nodesResponse] = await Promise.all([
          fetch(`https://api.grandchesstree.com/api/v3/perft/full/stats/charts/performance?account_id=${accountId}`),
          fetch(`https://api.grandchesstree.com/api/v3/perft/fast/stats/charts/performance?account_id=${accountId}`),
        ]);

        if (!statsResponse.ok || !nodesResponse.ok) {
          throw new Error("Failed to fetch data");
        }

        const statsData: RawDataPoint[] = await statsResponse.json();
        const nodesData: RawDataPoint[] = await nodesResponse.json();

        // Merge the two datasets by bucketing timestamps into 15-min intervals
        const mergedData: Record<number, DataPoint> = {};

        // Process stats data
        statsData.forEach((entry) => {
          const bucket = getBucket(entry.timestamp);
          if (!mergedData[bucket]) {
            mergedData[bucket] = {
              timestamp: bucket,
              nps_stats_task: 0,
              nps_nodes_task: 0,
            };
          }
          mergedData[bucket].nps_stats_task = entry.nps;
        });

        // Process nodes data
        nodesData.forEach((entry) => {
          const bucket = getBucket(entry.timestamp);
          if (!mergedData[bucket]) {
            mergedData[bucket] = {
              timestamp: bucket,
              nps_stats_task: 0,
              nps_nodes_task: 0,
            };
          }
          mergedData[bucket].nps_nodes_task = entry.nps;
        });

        // Convert merged data to an array and sort by timestamp
        const mergedArray: DataPoint[] = Object.values(mergedData);
        mergedArray.sort((a, b) => a.timestamp - b.timestamp);

        setData(mergedArray);
      } catch (error) {
        console.error("Error fetching data:", error);
      }
    };

    fetchData();
  }, []);

  return (
    <div className="w-full h-96 p-4 bg-gray-100 rounded-lg flex flex-col items-center">
      <span className="text-md font-bold m-2 text-gray-700">
        Total NPS (for stats and nodes tasks)
      </span>
      {data.length > 0 && (
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={data}>
            <CartesianGrid strokeDasharray="3 3" />
            <XAxis
              dataKey="timestamp"
              tickFormatter={formatTime}
              type="number"
              domain={["auto", "auto"]}
              tick={{ fontSize: 12 }}
              scale="time"
              interval="preserveStartEnd"
            />
            <YAxis
              yAxisId="left"
              orientation="left"
              tickFormatter={formatBigNumber}
              label={{ value: "NPS Stats Task", angle: -90, position: "insideLeft" }}
            />
            <YAxis
              yAxisId="right"
              orientation="right"
              tickFormatter={formatBigNumber}
              label={{ value: "NPS Nodes Task", angle: 90, position: "insideRight" }}
            />
            <Tooltip labelFormatter={formatTime} />
            <Legend />
            <Line
              yAxisId="left"
              type="monotone"
              dataKey="nps_stats_task"
              stroke="#82ca9d"
              name="NPS Stats Task"
            />
            <Line
              yAxisId="right"
              type="monotone"
              dataKey="nps_nodes_task"
              stroke="#8884d8"
              name="NPS Nodes Task"
            />
          </LineChart>
        </ResponsiveContainer>
      )}
    </div>
  );
};

export default AccountPerformanceChart;
