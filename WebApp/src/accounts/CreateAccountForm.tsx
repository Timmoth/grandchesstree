import React, { useState, FormEvent } from 'react';
import NavBar from '../NavBar';

const CreateAccountForm: React.FC = () => {
  const [name, setName] = useState<string>('');
  const [email, setEmail] = useState<string>('');
  const [errors, setErrors] = useState<{ name?: string; email?: string }>({});
  const [serverError, setServerError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(false);

  // Local validation function for name and email.
  const validate = (): boolean => {
    const validationErrors: { name?: string; email?: string } = {};

    // Validate name: min 3, max 25 and only alphanumeric, underscores, or hyphens.
    if (name.trim().length < 3) {
      validationErrors.name = 'Name must be at least 3 characters long.';
    } else if (name.trim().length > 25) {
      validationErrors.name = 'Name cannot exceed 25 characters.';
    } else if (!/^[a-zA-Z0-9_-]+$/.test(name.trim())) {
      validationErrors.name =
        'Name can only contain alphanumeric characters, underscores, and hyphens.';
    }

    // Validate email using a simple regex.
    if (!email.trim()) {
      validationErrors.email = 'Email is required.';
    } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) {
      validationErrors.email = 'Invalid email format.';
    }

    setErrors(validationErrors);
    return Object.keys(validationErrors).length === 0;
  };

  // Handle form submission.
  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setServerError(null);
    setSuccessMessage(null);

    if (!validate()) {
      return;
    }

    setIsLoading(true);
    try {
      const response = await fetch('https://api.grandchesstree.com/api/v1/accounts', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, email }),
      });

      if (response.ok) {
        setSuccessMessage('Account created successfully!');
        setName('');
        setEmail('');
      } else if (response.status === 409) {
        setServerError('An account with that name or email already exists.');
      } else {
        setServerError('An error occurred. Please try again.');
      }
    } catch (error) {
      setServerError('Network error. Please try again later.');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div>
    <NavBar />
    <div className="flex flex-col m-4 space-y-4 mt-20">
      <div className="space-y-4 p-4 bg-gray-100 rounded-lg text-gray-700 flex flex-col justify-between items-center space-x-4">
        <span className="text-md font-bold">Want to get involved?</span>
        <span className="text-sm font-semibold">
          If you're interested in volunteering computing resources or
          collaborating on the project
        </span>
        <span className="text-sm font-semibold">
          <a
            className="font-medium text-blue-600 hover:underline"
            href="https://discord.gg/cTu3aeCZVe"
            target="_blank"
            rel="noopener noreferrer"
          >
            join the Discord server!
          </a>
        </span>
      </div>
      
    <div className="max-w-md mx-auto mt-8 space-y-4 p-4 bg-gray-100 rounded-lg text-gray-700 flex flex-col justify-between items-center space-x-4">
      <form onSubmit={handleSubmit} className="px-8 pt-6 pb-8 mb-4">
        <h2 className="text-md font-bold mb-4">Create Account</h2>
        {serverError && <div className="mb-4 text-red-500">{serverError}</div>}
        {successMessage && <div className="mb-4 text-green-500">{successMessage}</div>}

        <div className="mb-4">
          <label htmlFor="name" className="block text-gray-700 text-sm mb-2">
            Name
          </label>
          <input
            id="name"
            type="text"
            value={name}
            onChange={(e) => setName(e.target.value)}
            className={`shadow appearance-none border rounded w-full py-2 px-3 text-gray-700 leading-tight focus:outline-none focus:shadow-outline ${
              errors.name ? 'border-red-500' : ''
            }`}
          />
          {errors.name && <p className="text-red-500 text-xs italic">{errors.name}</p>}
        </div>

        <div className="mb-4">
          <label htmlFor="email" className="block text-gray-700 text-sm mb-2">
            Email
          </label>
          <input
            id="email"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className={`shadow appearance-none border rounded w-full py-2 px-3 text-gray-700 leading-tight focus:outline-none focus:shadow-outline ${
              errors.email ? 'border-red-500' : ''
            }`}
          />
          {errors.email && <p className="text-red-500 text-xs italic">{errors.email}</p>}
        </div>

        <div className="flex items-center justify-between">
          <button
            type="submit"
            disabled={isLoading}
            className="bg-blue-500 hover:bg-blue-700 text-white font-bold py-2 px-4 rounded focus:outline-none focus:shadow-outline"
          >
            {isLoading ? 'Creating...' : 'Create Account'}
          </button>
        </div>
      </form>
    </div>
    </div>
    </div>
  );
};

export default CreateAccountForm;
