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

interface DataPoint {
  timestamp: number;
  tpm: number;
  nps: number;
}

const formatBigNumber = (num: number): string => {
  if (num >= 1e12) return (num / 1e12).toFixed(1) + "t"; // Trillion
  if (num >= 1e9) return (num / 1e9).toFixed(1) + "b"; // Billion
  if (num >= 1e6) return (num / 1e6).toFixed(1) + "m"; // Million
  if (num >= 1e3) return (num / 1e3).toFixed(1) + "k"; // Thousand
  return num.toString();
};

const formatTime = (timestamp: number): string => {
  const date = new Date(timestamp * 1000);
  return date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
};

interface PerformanceChartProps {
  positionId: number,
  depth: number,
}
const PerftNodesPerformanceChart: React.FC<PerformanceChartProps> = ({ positionId, depth }) => {
  const [data, setData] = useState<DataPoint[]>([]);

  useEffect(() => {
    const fetchData = async () => {
      try {
        const response = await fetch(
          `https://api.grandchesstree.com/api/v3/perft/fast/${positionId}/${depth}/stats/charts/performance`
        );
        const rawData: DataPoint[] = await response.json();
        setData(rawData);
      } catch (error) {
        console.error("Error fetching data:", error);
      }
    };

    fetchData();
  }, [positionId, depth]);

  return (
    <div className="w-full h-96 p-4 bg-gray-100 rounded-lg flex flex-col items-center">
      <span className="text-md font-bold m-2 text-gray-700">
        Performance over time
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
              label={{ value: "TPM", angle: -90, position: "insideLeft" }}
            />
            <YAxis
              yAxisId="right"
              orientation="right"
              tickFormatter={formatBigNumber}
              label={{ value: "NPS", angle: -90, position: "insideRight" }}
            />
            <Tooltip labelFormatter={formatTime} />
            <Legend />
            <Line
              yAxisId="left"
              type="monotone"
              dataKey="tpm"
              stroke="#8884d8"
              name="TPM"
            />
            <Line
              yAxisId="right"
              type="monotone"
              dataKey="nps"
              stroke="#82ca9d"
              name="NPS"
            />
          </LineChart>
        </ResponsiveContainer>
      )}
    </div>
  );
};

export default PerftNodesPerformanceChart;
