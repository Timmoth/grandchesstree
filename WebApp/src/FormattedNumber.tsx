import React from "react";

interface FormattedNumberProps {
  value: number;
  min: number;
  max: number;
}

const predefinedClasses = [
  "text-gray-300", "text-gray-400", "text-gray-500", // Common
  "text-green-400", "text-green-500", "text-green-600", // Uncommon
  "text-blue-400", "text-blue-500", "text-blue-600", // Rare
  "text-purple-400", "text-purple-500", "text-purple-600", // Epic
  "text-orange-400", "text-orange-500", "text-orange-600"  // Legendary
];

const interpolateColor = (num: number, min: number, max: number): string => {
  if (num <= min) return predefinedClasses[0];
  if (num >= max) return predefinedClasses[predefinedClasses.length - 1];
  
  const logMin = Math.log10(min);
  const logMax = Math.log10(max);
  const logValue = Math.log10(num);
  const scale = (logValue - logMin) / (logMax - logMin);
  
  const index = Math.floor(scale * (predefinedClasses.length - 1));
  return predefinedClasses[index];
};

const formatBigNumber = (num: number): string => {
  if (num >= 1e15) return (num / 1e15).toFixed(1) + "q"; // Quadrillion
  if (num >= 1e12) return (num / 1e12).toFixed(1) + "t"; // Trillion
  if (num >= 1e9) return (num / 1e9).toFixed(1) + "b"; // Billion
  if (num >= 1e6) return (num / 1e6).toFixed(1) + "m"; // Million
  if (num >= 1e3) return (num / 1e3).toFixed(1) + "k"; // Thousand
  return num.toString(); // Return as is if it's less than 1000
};

const FormattedNumber: React.FC<FormattedNumberProps> = ({ value, min, max }) => {
  return <span className={interpolateColor(value, min, max)}>{formatBigNumber(value)}</span>;
};

export default FormattedNumber;