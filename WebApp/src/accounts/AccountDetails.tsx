import { useState, useEffect } from "react";

interface TaskData {
  total_nodes: number;
  compute_time_seconds: number;
  completed_tasks: number;
  tpm: number;
  nps: number;
}

interface AccountData {
  id: number;
  name: string;
  task_0: TaskData;
  task_1: TaskData;
}

interface AccountDetailsProps {
  accountId: number;
}

const AccountDetails: React.FC<AccountDetailsProps> = ({ accountId }) => {
  const [account, setAccount] = useState<AccountData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const fetchAccount = async () => {
      try {
        setLoading(true);
        setError(null);
        const response = await fetch(
          `https://api.grandchesstree.com/api/v1/accounts/${accountId}`
        );

        if (!response.ok) {
          throw new Error(`Failed to fetch: ${response.statusText}`);
        }

        const data: AccountData = await response.json();
        setAccount(data);
      } catch (err) {
        setError(
          err instanceof Error ? err.message : "An unknown error occurred"
        );
      } finally {
        setLoading(false);
      }
    };

    fetchAccount();
  }, [accountId]);

  // Format large numbers (e.g., 1000 -> 1k, 1000000 -> 1m)
  const formatBigNumber = (num: number): string => {
    if (num >= 1e12) return (num / 1e12).toFixed(1) + "t"; // Trillion
    if (num >= 1e9) return (num / 1e9).toFixed(1) + "b"; // Billion
    if (num >= 1e6) return (num / 1e6).toFixed(1) + "m"; // Million
    if (num >= 1e3) return (num / 1e3).toFixed(1) + "k"; // Thousand
    return num.toString(); // Return as is if it's less than 1000
  };

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

  if (loading) return <p className="text-gray-500">Loading...</p>;
  if (error) return <p className="text-red-500">Error: {error}</p>;

  return (
    <>
      <div className="flex flex-col space-x-4 p-4 bg-gray-100 rounded-lg text-gray-700">
        <div className="flex justify-between items-center space-x-4">
          <span className="text-md font-semibold">Name</span>
          <span className="text-xl font-bold">{account?.name}</span>
        </div>
        <div className="flex justify-between items-center space-x-4">
          <span className="text-md font-semibold">Status</span>
          <span className="text-xl font-bold">{((account!.task_0.nps + account!.task_1.nps) > 0 ? <span className="text-green-500">active</span>:<span>offline</span>)}</span>
        </div>
        <div className="flex items-stretch">
          <div className="space-y-4 p-4 bg-gray-100 rounded-lg text-gray-700">
            <span className="text-lg font-bold">Task 0 (stats)</span>

            <div className="flex justify-between items-center space-x-4">
              <span className="text-md font-semibold">Completed Tasks</span>
              <span className="text-xl font-bold">
                {account && formatBigNumber(account?.task_0.completed_tasks)}
              </span>
            </div>

            <div className="flex justify-between items-center space-x-4">
              <span className="text-md font-semibold">Compute Time</span>
              <span className="text-xl font-bold">
                {account && formatTime(account?.task_0.compute_time_seconds)}
              </span>
            </div>

            <div className="flex justify-between items-center space-x-4">
              <span className="text-md font-semibold">Total Nodes</span>
              <span className="text-xl font-bold">
                {account && formatBigNumber(account?.task_0.total_nodes)}
              </span>
            </div>
            <div className="flex justify-between items-center space-x-4">
              <span className="text-md font-semibold">NPS</span>
              <span className="text-xl font-bold">
                {account && formatBigNumber(account?.task_0.nps)}
              </span>
            </div>
            <div className="flex justify-between items-center space-x-4">
              <span className="text-md font-semibold">TPM</span>
              <span className="text-xl font-bold">
                {account && formatBigNumber(account?.task_0.tpm)}
              </span>
            </div>
          </div>

          <div className="space-y-4 p-4 bg-gray-100 rounded-lg text-gray-700">
            <span className="text-lg font-bold">Task 1 (nodes)</span>

            <div className="flex justify-between items-center space-x-4">
              <span className="text-md font-semibold">Completed Tasks</span>
              <span className="text-xl font-bold">
                {account && formatBigNumber(account?.task_1.completed_tasks)}
              </span>
            </div>

            <div className="flex justify-between items-center space-x-4">
              <span className="text-md font-semibold">Compute Time</span>
              <span className="text-xl font-bold">
                {account && formatTime(account?.task_1.compute_time_seconds)}
              </span>
            </div>

            <div className="flex justify-between items-center space-x-4">
              <span className="text-md font-semibold">Total Nodes</span>
              <span className="text-xl font-bold">
                {account && formatBigNumber(account?.task_1.total_nodes)}
              </span>
            </div>
            <div className="flex justify-between items-center space-x-4">
              <span className="text-md font-semibold">NPS</span>
              <span className="text-xl font-bold">
                {account && formatBigNumber(account?.task_1.nps)}
              </span>
            </div>
            <div className="flex justify-between items-center space-x-4">
              <span className="text-md font-semibold">TPM</span>
              <span className="text-xl font-bold">
                {account && formatBigNumber(account?.task_1.tpm)}
              </span>
            </div>
          </div>
        </div>
      </div>
    </>
  );
};

export default AccountDetails;
