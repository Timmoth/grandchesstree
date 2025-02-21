import React, { useEffect, useState } from "react";

// Type definition for the leaderboard response
interface PerftLeaderboardResponse {
  account_name: string;
  total_nodes: number;
  compute_time_seconds: number;
  nps: number;
}

// New type for merged leaderboard entries
interface PerftLeaderBoardEntry {
  account_name: string;
  perft_stats_task_nodes: number;
  perft_nodes_task_nodes: number;
  compute_time_seconds: number;
  nps_stats_task: number;
  nps_nodes_task: number;
}

const GlobalLeaderboard: React.FC = () => {
  const [leaderboardData, setLeaderboardData] = useState<PerftLeaderBoardEntry[]>(
    []
  );
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const fetchLeaderboards = async () => {
      try {
        const [statsResponse, nodesResponse] = await Promise.all([
          fetch("https://api.grandchesstree.com/api/v2/perft/leaderboard"),
          fetch("https://api.grandchesstree.com/api/v2/perft/nodes/leaderboard"),
        ]);

        if (!statsResponse.ok || !nodesResponse.ok) {
          throw new Error("Failed to fetch data");
        }

        const statsData: PerftLeaderboardResponse[] = await statsResponse.json();
        const nodesData: PerftLeaderboardResponse[] = await nodesResponse.json();

        // Merging the data based on account_name
        const mergedData: Record<string, PerftLeaderBoardEntry> = {};

        const processEntry = (
          entry: PerftLeaderboardResponse,
          isStats: boolean
        ) => {
          if (!mergedData[entry.account_name]) {
            mergedData[entry.account_name] = {
              account_name: entry.account_name,
              perft_stats_task_nodes: 0,
              perft_nodes_task_nodes: 0,
              compute_time_seconds: 0,
              nps_stats_task: 0,
              nps_nodes_task: 0,
            };
          }

          const target = mergedData[entry.account_name];
          if (isStats) {
            target.perft_stats_task_nodes = entry.total_nodes;
            target.nps_stats_task = entry.nps;
          } else {
            target.perft_nodes_task_nodes = entry.total_nodes;
            target.nps_nodes_task = entry.nps;
          }
          target.compute_time_seconds += entry.compute_time_seconds;
        };

        statsData.forEach((entry) => processEntry(entry, true));
        nodesData.forEach((entry) => processEntry(entry, false));

        setLeaderboardData(Object.values(mergedData));
      } catch (err: any) {
        setError(err.message);
      } finally {
        setLoading(false);
      }
    };

    fetchLeaderboards();
  }, []);

  const formatTime = (seconds: number): string => {
    if (seconds < 60) return `${seconds}s`;
    if (seconds < 3600) return `${Math.floor(seconds / 60)}m ${seconds % 60}s`;
    if (seconds < 86400)
      return `${Math.floor(seconds / 3600)}h ${Math.floor((seconds % 3600) / 60)}m`;
    return `${Math.floor(seconds / 86400)}d`;
  };

  const formatBigNumber = (num: number): string => {
    if (num >= 1e12) return (num / 1e12).toFixed(1) + "t";
    if (num >= 1e9) return (num / 1e9).toFixed(1) + "b";
    if (num >= 1e6) return (num / 1e6).toFixed(1) + "m";
    if (num >= 1e3) return (num / 1e3).toFixed(1) + "k";
    return num.toString();
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
                  <td className="px-6 py-4">{item.account_name}</td>
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

export default GlobalLeaderboard;
