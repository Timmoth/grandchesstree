import React, { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import FormattedNumber from "../FormattedNumber";

// Type definition for the leaderboard response
interface PerftLeaderboardResponse {
  account_id:number;
  account_name: string;
  total_nodes: number;
  completed_tasks: number;
  tpm: number;
  nps: number;
}

interface LeaderboardProps {
  positionId: number,
  depth: number,
}
const Leaderboard: React.FC<LeaderboardProps> = ({ positionId, depth  }) => {
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
          `https://api.grandchesstree.com/api/v3/perft/full/${positionId}/${depth}/leaderboard`
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
                    <FormattedNumber value={item.total_nodes} min={1e9} max={1e16}/>
                    </td>
                    <td className="px-6 py-4">
                      <FormattedNumber value={item.completed_tasks} min={1e3} max={1e7}/>
                    </td>
                    <td className="px-6 py-4"><FormattedNumber value={item.tpm} min={1} max={1e3}/></td>
                    <td className="px-6 py-4"><FormattedNumber value={item.nps} min={1e8} max={1e11}/></td>

                  </tr>
                ))}
            </tbody>
          </table>
        </div>
      </div>
    </>
  );
};

export default Leaderboard;
