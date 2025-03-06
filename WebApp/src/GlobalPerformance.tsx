import { useState, useEffect } from "react";
import { formatBigNumber, FormatMB } from "./Utils";

interface GlobalPerformanceData {
    workers: number;
    threads: number;
    allocated_mb: number;
    mips: number;
}

const GlobalPerformance: React.FC = () => {
  const [account, setAccount] = useState<GlobalPerformanceData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const fetchAccount = async () => {
      try {
        setLoading(true);
        setError(null);
        const response = await fetch(
          `https://api.grandchesstree.com/api/v4/performance`
        );

        if (!response.ok) {
          throw new Error(`Failed to fetch: ${response.statusText}`);
        }

        const data: GlobalPerformanceData = await response.json();
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
  }, []);

  if (loading) return <p className="text-gray-500">Loading...</p>;
  if (error) return <p className="text-red-500">Error: {error}</p>;

  return (
    <>
      <div className="flex flex-col space-x-4 p-4 bg-gray-100 rounded-lg text-gray-700">
      <span className="text-md font-bold">
                Global Performance
              </span>
        <div className="flex justify-between items-center space-x-4">
          <span className="text-md font-semibold">Workers:</span>
          <span className="text-xl font-bold">{account?.workers}</span>
        </div>
        <div className="flex justify-between items-center space-x-4">
          <span className="text-md font-semibold">Threads:</span>
          <span className="text-xl font-bold">{account?.threads}</span>
        </div>
        <div className="flex justify-between items-center space-x-4">
          <span className="text-md font-semibold">Memory:</span>
          <span className="text-xl font-bold">{FormatMB(account?.allocated_mb ?? 0)}</span>
        </div>
        <div className="flex justify-between items-center space-x-4">
          <span className="text-md font-semibold">MIPS:</span>
          <span className="text-xl font-bold">{formatBigNumber(account?.mips ?? 0)}</span>
        </div>
        </div>
    </>
  );
};

export default GlobalPerformance;
