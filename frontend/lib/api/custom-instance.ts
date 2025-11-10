/**
 * Custom fetch instance with authentication and error handling
 */

// const API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5087";
const API_BASE_URL = "";

export type ErrorType<Error> = Error;

export const customInstance = async <T>(
  url: string,
  config?: RequestInit & { params?: Record<string, unknown> }
): Promise<T> => {
  const { params, ...rest } = config || {};

  const searchParams = new URLSearchParams();
  if (params) {
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null) {
        searchParams.append(key, String(value));
      }
    });
  }

  const queryString = searchParams.toString();
  const fullUrl = `${API_BASE_URL}${url}${queryString ? `?${queryString}` : ""}`;

  const token = typeof window !== "undefined" ? localStorage.getItem("accessToken") : null;

  const response = await fetch(fullUrl, {
    ...rest,
    headers: {
      "Content-Type": "application/json",
      ...(token && { Authorization: `Bearer ${token}` }),
      ...rest.headers,
    },
  });

  // Parse response data
  let data;
  if (response.status === 204) {
    data = {};
  } else {
    data = await response.json();
  }

  // Return format that matches Orval's expectation
  const result = {
    data,
    status: response.status,
    headers: response.headers,
  } as T;

  if (!response.ok) {
    throw result; // Throw với format đầy đủ
  }

  return result;
};