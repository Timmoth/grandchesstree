import React, { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import FormattedNumber from "../FormattedNumber";

// Type definition for the leaderboard response
interface PerftLeaderboardResponse {
  account_id:string;
  account_name: string;
  total_nodes: number;
  nps: number;
}

// New type for merged leaderboard entries
interface PerftLeaderBoardEntry {
  account_id:string;
  account_name: string;
  perft_stats_task_nodes: number;
  perft_nodes_task_nodes: number;
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
          fetch("https://api.grandchesstree.com/api/v3/perft/full/leaderboard"),
          fetch("https://api.grandchesstree.com/api/v3/perft/fast/leaderboard"),
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
              account_id: entry.account_id,
              account_name: entry.account_name,
              perft_stats_task_nodes: 0,
              perft_nodes_task_nodes: 0,
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
              <th className="px-6 py-3">Full task nodes</th>
              <th className="px-6 py-3">Full task nps</th>
              <th className="px-6 py-3">Fast task nodes</th>
              <th className="px-6 py-3">Fast task nps</th>
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
                  <td className="px-6 py-4"><FormattedNumber value={item.perft_stats_task_nodes} min={1e9} max={1e16}/></td>
                  <td className="px-6 py-4"><FormattedNumber value={item.nps_stats_task} min={1e8} max={1e11}/></td>
                  <td className="px-6 py-4"><FormattedNumber value={item.perft_nodes_task_nodes} min={1e9} max={1e16}/></td>
                  <td className="px-6 py-4"><FormattedNumber value={item.nps_nodes_task} min={1e8} max={1e12}/></td>
                </tr>
              ))}
          </tbody>
        </table>
      </div>
    </div>
  );
};

export default GlobalLeaderboard;
