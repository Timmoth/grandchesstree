import React, { useEffect, useState } from "react";
import { Link } from "react-router-dom";

// Type definition for the leaderboard response
interface PerftLeaderboardResponse {
  account_id:string;
  account_name: string;
  total_nodes: number;
  compute_time_seconds: number;
  nps: number;
}

// New type for merged leaderboard entries
interface PerftLeaderBoardEntry {
  account_id:string;
  account_name: string;
  perft_stats_task_nodes: number;
  perft_nodes_task_nodes: number;
  compute_time_seconds: number;
  nps_stats_task: number;
  nps_nodes_task: number;
}

const GlobalLeaderboardAllTime: React.FC = () => {

    const [data, setData] = useState<PerftSummary | null>(null);
    const [loading, setLoading] = useState<boolean>(true);
    const [error, setError] = useState<string | null>(null);
    const formatBigNumber = (num: number): string => {
      if (num >= 1e12) return (num / 1e12).toFixed(1) + "t"; // Trillion
      if (num >= 1e9) return (num / 1e9).toFixed(1) + "b"; // Billion
      if (num >= 1e6) return (num / 1e6).toFixed(1) + "m"; // Million
      if (num >= 1e3) return (num / 1e3).toFixed(1) + "k"; // Thousand
      return num.toString();
    };
 
    useEffect(() => {
      fetch(`/perft_p${positionId}_results.json`)
        .then((response) => {
          if (!response.ok) {
            throw new Error("Failed to fetch data");
          }
          return response.json();
        })
        .then((jsonData) => {
          setData(jsonData);
          setLoading(false);
        })
        .catch((err) => {
          setError(err.message);
          setLoading(false);
        });
    }, [positionId]);
    
    if (loading || error || !data) {
      return (
        <>
          <div>
            Loading...
          </div>
        </>
      );
    }

  const formatTime = (seconds: number): string => {
    if (seconds < 60) return `${seconds}s`;
    if (seconds < 3600) return `${Math.floor(seconds / 60)}m ${seconds % 60}s`;
    if (seconds < 86400)
      return `${Math.floor(seconds / 3600)}h ${Math.floor((seconds % 3600) / 60)}m`;
    return `${Math.floor(seconds / 86400)}d`;
  };

  if (loading) return <p>Loading...</p>;
  if (error) return <p>Error: {error}</p>;

  return (
    <div className="relative bg-gray-100 rounded-lg p-4 flex flex-col justify-between items-center">
      <span className="text-md font-bold m-2 text-gray-700">Top Contributors</span>
      <div className="w-full overflow-x-auto">
        <table className="min-w-[1000px] text-sm text-left rtl:text-right text-gray-500">
          <thead className="text-xs text-gray-700 uppercase">
            <tr>
              <th className="px-6 py-3">Name</th>
              <th className="px-6 py-3">Status</th>
              <th className="px-6 py-3">Compute Time</th>
              <th className="px-6 py-3">Perft Stats Nodes</th>
              <th className="px-6 py-3">NPS Stats Task</th>
              <th className="px-6 py-3">Perft Nodes Task Nodes</th>
              <th className="px-6 py-3">NPS Nodes Task</th>
            </tr>
          </thead>
          <tbody>
            {leaderboardData
              .sort((a, b) => b.perft_stats_task_nodes + b.perft_nodes_task_nodes - (a.perft_stats_task_nodes + a.perft_nodes_task_nodes))
              .map((item, index) => (
                <tr key={index} className="bg-white border-b border-gray-200">
                  <td className="px-6 py-4">
                  <Link
                    className="font-medium text-blue-600 hover:underline"
                    to={`/accounts/${item.account_id}`}
                  >
                     {item.account_name}
                  </Link>
                 </td>
                  <td className="px-6 py-4 ">{((item.nps_stats_task + item.nps_nodes_task) > 0 ? <span className="text-green-500">active</span>:<span>offline</span>)}</td>
                  <td className="px-6 py-4">{formatTime(item.compute_time_seconds)}</td>
                  <td className="px-6 py-4">{formatBigNumber(item.perft_stats_task_nodes)}</td>
                  <td className="px-6 py-4">{formatBigNumber(item.nps_stats_task)}</td>
                  <td className="px-6 py-4">{formatBigNumber(item.perft_nodes_task_nodes)}</td>
                  <td className="px-6 py-4">{formatBigNumber(item.nps_nodes_task)}</td>
                </tr>
              ))}
          </tbody>
        </table>
      </div>
    </div>
  );
};

export default GlobalLeaderboardAllTime;
