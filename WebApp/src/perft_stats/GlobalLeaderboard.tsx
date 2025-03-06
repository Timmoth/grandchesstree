import React, { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import FormattedNumber from "../FormattedNumber";
import { formatBigNumber, FormatMB } from "../Utils";

// Type definition for the leaderboard response
interface PerftLeaderboardResponse {
  account_id:string;
  account_name: string;
  total_nodes: number;
  nps: number;
  total_tasks: number;
  tpm: number;
  workers: number;
  threads: number;
  allocated_mb: number;
  mips: number;
}

// New type for merged leaderboard entries
interface PerftLeaderBoardEntry {
  account_id:string;
  account_name: string;
  perft_stats_task_nodes: number;
  perft_stats_tasks: number;
  perft_nodes_task_nodes: number;
  perft_nodes_tasks: number;
  nps_stats_task: number;
  tpm_stats_task: number;
  nps_nodes_task: number;
  tpm_nodes_task: number;
  workers: number;
  threads: number;
  allocated_mb: number;
  mips: number;
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
          fetch("https://api.grandchesstree.com/api/v4/perft/full/leaderboard"),
          fetch("https://api.grandchesstree.com/api/v4/perft/fast/leaderboard"),
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
              perft_stats_tasks: 0,
              perft_nodes_task_nodes: 0,
              perft_nodes_tasks: 0,
              nps_stats_task: 0,
              tpm_stats_task: 0,
              nps_nodes_task: 0,
              tpm_nodes_task: 0,
              workers: 0,
              threads: 0,
              allocated_mb: 0,
              mips: 0,
            };
          }

          const target = mergedData[entry.account_name];

          target.workers += entry.workers;
          target.threads += entry.threads;
          target.allocated_mb += entry.allocated_mb;
          target.mips += entry.mips;

          if (isStats) {
            target.perft_stats_task_nodes = entry.total_nodes;
            target.perft_stats_tasks = entry.total_tasks;
            target.nps_stats_task = entry.nps;
            target.tpm_stats_task = entry.tpm;
          } else {
            target.perft_nodes_task_nodes = entry.total_nodes;
            target.perft_nodes_tasks = entry.total_tasks;
            target.nps_nodes_task = entry.nps;
            target.tpm_nodes_task = entry.tpm;
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
              <th className="px-6 py-3">Threads</th>
              <th className="px-6 py-3">Memory</th>
              <th className="px-6 py-3">MIPS</th>
              <th className="px-6 py-3">Full tasks</th>
              <th className="px-6 py-3">tpm</th>
              <th className="px-6 py-3">nodes</th>
              <th className="px-6 py-3">nps</th>
              <th className="px-6 py-3">Fast tasks</th>
              <th className="px-6 py-3">tpm</th>
              <th className="px-6 py-3">nodes</th>
              <th className="px-6 py-3">nps</th>
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
                  <td className="px-6 py-4">{item.threads}</td>
                  <td className="px-6 py-4">{FormatMB(item.allocated_mb)}</td>
                  <td className="px-6 py-4">{formatBigNumber(item.mips)}</td>
                  <td className="px-6 py-4"><FormattedNumber value={item.perft_stats_tasks} min={1e1} max={1e7}/></td>
                  <td className="px-6 py-4"><FormattedNumber value={item.tpm_stats_task} min={1e1} max={1e3}/></td>
                  <td className="px-6 py-4"><FormattedNumber value={item.perft_stats_task_nodes} min={1e9} max={1e16}/></td>
                  <td className="px-6 py-4"><FormattedNumber value={item.nps_stats_task} min={1e8} max={1e11}/></td>
                  <td className="px-6 py-4"><FormattedNumber value={item.perft_nodes_tasks} min={1e1} max={1e7}/></td>
                  <td className="px-6 py-4"><FormattedNumber value={item.tpm_nodes_task} min={1e1} max={1e3}/></td>
                  <td className="px-6 py-4"><FormattedNumber value={item.perft_nodes_task_nodes} min={1e9} max={1e16}/></td>
                  <td className="px-6 py-4"><FormattedNumber value={item.nps_nodes_task} min={1e8} max={1e11}/></td>
                </tr>
              ))}
          </tbody>
        </table>
      </div>
    </div>
  );
};

export default GlobalLeaderboard;
