import { useState, useEffect } from "react";
import FormattedNumber from "../FormattedNumber";

interface TaskData {
  total_nodes: number;
  completed_tasks: number;
  tpm: number;
  nps: number;
}

interface WorkerStats {
  worker_id: number;
  task_type: number;
  nps: number;
  tpm: number;
}

interface AccountData {
  id: number;
  name: string;
  task_0: TaskData;
  task_1: TaskData;
  workers: WorkerStats[];
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
            <span className="text-lg font-bold">Full task</span>

            <div className="flex justify-between items-center space-x-4">
              <span className="text-md font-semibold">Completed Tasks</span>
              <span className="text-xl font-bold">
                {account && <FormattedNumber value={account?.task_0.completed_tasks} min={1e3} max={1e7}/>}
              </span>
            </div>

            <div className="flex justify-between items-center space-x-4">
              <span className="text-md font-semibold">Total Nodes</span>
              <span className="text-xl font-bold">
                {account && <FormattedNumber value={account?.task_0.total_nodes} min={1e9} max={1e16}/>}
              </span>
            </div>
            <div className="flex justify-between items-center space-x-4">
              <span className="text-md font-semibold">NPS</span>
              <span className="text-xl font-bold">
                {account && <FormattedNumber value={account?.task_0.nps}min={1e4} max={1e14}/>}
              </span>
            </div>
            <div className="flex justify-between items-center space-x-4">
              <span className="text-md font-semibold">TPM</span>
              <span className="text-xl font-bold">
                {account && <FormattedNumber value={account?.task_0.tpm}min={1} max={1e4}/>}
              </span>
            </div>
          </div>

          <div className="space-y-4 p-4 bg-gray-100 rounded-lg text-gray-700">
            <span className="text-lg font-bold">Fast task</span>

            <div className="flex justify-between items-center space-x-4">
              <span className="text-md font-semibold">Completed Tasks</span>
              <span className="text-xl font-bold">
                {account && <FormattedNumber value={account?.task_1.completed_tasks} min={1e3} max={1e7}/>}
              </span>
            </div>

            <div className="flex justify-between items-center space-x-4">
              <span className="text-md font-semibold">Total Nodes</span>
              <span className="text-xl font-bold">
                {account && <FormattedNumber value={account?.task_1.total_nodes}min={1e9} max={1e16}/>}
              </span>
            </div>
            <div className="flex justify-between items-center space-x-4">
              <span className="text-md font-semibold">NPS</span>
              <span className="text-xl font-bold">
                {account && <FormattedNumber value={account?.task_1.nps}min={1e4} max={1e14}/>}
              </span>
            </div>
            <div className="flex justify-between items-center space-x-4">
              <span className="text-md font-semibold">TPM</span>
              <span className="text-xl font-bold">
                {account && <FormattedNumber value={account?.task_1.tpm}min={1} max={1e4}/>}
              </span>
            </div>
          </div>

          <div className="w-full overflow-x-auto">
          <table className="min-w-[400px] text-sm text-left rtl:text-right text-gray-500">
            <thead className="text-xs text-gray-700 uppercase">
              <tr>
                <th scope="col" className="px-6 py-3">
                  Worker Id
                </th>
                <th scope="col" className="px-6 py-3">
                  Task Type
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
              {account?.workers.map((item, index) => (
                <tr key={index} className="bg-white border-b border-gray-200">
                  <td className="px-6 py-4">{item.worker_id}</td>
                  <td className="px-6 py-4">{item.task_type}</td>
                  <td className="px-6 py-4"><FormattedNumber value={item.tpm} min={1} max={1e4}/></td>
                  <td className="px-6 py-4"><FormattedNumber value={item.nps} min={1e8} max={1e12}/></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        </div>
      </div>
    </>
  );
};

export default AccountDetails;
