import React, { useEffect, useState } from "react";
import { Link } from "react-router-dom";

// Type definition for the leaderboard response
interface PerftLeaderboardResponse {
  account_id: number;
  account_name: string;
  total_nodes: number;
  compute_time_seconds: number;
  completed_tasks: number;
  tpm: number;
  nps: number;
}

interface LeaderboardProps {
  positionId: number,
  depth: number,
}
const PerftNodesLeaderboard: React.FC<LeaderboardProps> = ({ positionId, depth  }) => {
  const [leaderboardData, setLeaderboardData] = useState<
    PerftLeaderboardResponse[]
  >([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    // Fetch the leaderboard data from the API
    const fetchLeaderboard = async () => {
      try {
        const resp = await fetch(
          `https://api.grandchesstree.com/api/v2/perft/nodes/${positionId}/${depth}/leaderboard`
        );
        if (!resp.ok) {
          throw new Error("Failed to fetch data");
        }
        const data: PerftLeaderboardResponse[] = await resp.json(); // Explicitly typing the response
        setLeaderboardData(data); // Store the data in state
      } catch (err: any) {
        setError(err.message); // Store error message if there's an issue with the fetch
      } finally {
        setLoading(false); // Set loading to false once data is fetched
      }
    };

    fetchLeaderboard();
  }, [positionId, depth]); // Empty dependency array ensures this effect runs only once when the component mounts

  // Format time to human-readable form (hours, minutes, seconds)
  const formatTime = (seconds: number): string => {
    if (seconds < 60) {
      return `${seconds}s`; // Less than 1 minute
    } else if (seconds < 3600) {
      const minutes = Math.floor(seconds / 60);
      const remainingSeconds = seconds % 60;
      return `${minutes}m ${remainingSeconds}s`; // Less than 1 hour
    } else if (seconds < 86400) {
      const hours = Math.floor(seconds / 3600);
      const remainingMinutes = Math.floor((seconds % 3600) / 60);
      return `${hours}h ${remainingMinutes}m`; // Less than 1 day
    } else {
      const days = Math.floor(seconds / 86400);
      return `${days}d`; // More than 1 day
    }
  };

  // Format large numbers (e.g., 1000 -> 1k, 1000000 -> 1m)
  const formatBigNumber = (num: number): string => {
    if (num >= 1e12) return (num / 1e12).toFixed(1) + "t"; // Trillion
    if (num >= 1e9) return (num / 1e9).toFixed(1) + "b"; // Billion
    if (num >= 1e6) return (num / 1e6).toFixed(1) + "m"; // Million
    if (num >= 1e3) return (num / 1e3).toFixed(1) + "k"; // Thousand
    return num.toString(); // Return as is if it's less than 1000
  };

  if (loading) {
    return <p>Loading...</p>;
  }

  if (error) {
    return <p>Error: {error}</p>;
  }

  return (
    <>
      <div className="relative bg-gray-100 rounded-lg p-4 flex flex-col justify-between items-center">
        <span className="text-md font-bold m-2 text-gray-700">
          Top contributors
        </span>
        <div className="w-full overflow-x-auto">
          <table className="min-w-[1000px] text-sm text-left rtl:text-right text-gray-500">
            <thead className="text-xs text-gray-700 uppercase">
              <tr>
                <th scope="col" className="px-6 py-3">
                  Name
                </th>
                <th scope="col" className="px-6 py-3">
                  Status
                </th>
                <th scope="col" className="px-6 py-3">
                  Total Nodes
                </th>
                <th scope="col" className="px-6 py-3">
                  Compute Time
                </th>
                <th scope="col" className="px-6 py-3">
                  Completed Tasks
                </th>
                <th scope="col" className="px-6 py-3">
                  TPM
                </th>
                <th scope="col" className="px-6 py-3">
                  NPS
                </th>
              </tr>
            </thead>
            <tbody>
              {leaderboardData
                .sort((a, b) => b.total_nodes - a.total_nodes) // Sort by total_nodes in descending order
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
                  <td className="px-6 py-4 ">{((item.nps) > 0 ? <span className="text-green-500">active</span>:<span>offline</span>)}</td>
                    <td className="px-6 py-4">
                      {formatBigNumber(item.total_nodes)}
                    </td>
                    <td className="px-6 py-4">
                      {formatTime(item.compute_time_seconds)}
                    </td>
                    <td className="px-6 py-4">
                      {formatBigNumber(item.completed_tasks)}
                    </td>
                    <td className="px-6 py-4">{formatBigNumber(item.tpm)}</td>
                    <td className="px-6 py-4">{formatBigNumber(item.nps)}</td>

                  </tr>
                ))}
            </tbody>
          </table>
        </div>
      </div>
    </>
  );
};

export default PerftNodesLeaderboard;
