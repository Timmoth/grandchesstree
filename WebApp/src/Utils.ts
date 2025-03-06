export function FormatMB(mb: number): string {
    const gb = mb / 1024; // 1 GB = 1024 MB
    return `${gb.toFixed(2)} GB`;
}

export function formatBigNumber(num: number): string {
    if (num >= 1e15) return (num / 1e15).toFixed(1) + "q"; // Quadrillion
    if (num >= 1e12) return (num / 1e12).toFixed(1) + "t"; // Trillion
    if (num >= 1e9) return (num / 1e9).toFixed(1) + "b"; // Billion
    if (num >= 1e6) return (num / 1e6).toFixed(1) + "m"; // Million
    if (num >= 1e3) return (num / 1e3).toFixed(1) + "k"; // Thousand
    return num.toString(); // Return as is if it's less than 1000
  };