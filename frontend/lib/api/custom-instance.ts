import axios, { AxiosRequestConfig, AxiosResponse, AxiosError } from 'axios';

const API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL || 'http://localhost:5087';

// Tạo axios instance
const axiosInstance = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

// Request interceptor - thêm token vào mỗi request
axiosInstance.interceptors.request.use(
  config => {
    // Chỉ lấy token ở client-side
    if (typeof window !== 'undefined') {
      const token = localStorage.getItem('accessToken');
      if (token) {
        config.headers.Authorization = `Bearer ${token}`;
      }
    }
    return config;
  },
  error => {
    return Promise.reject(error);
  },
);

// Response interceptor - xử lý lỗi tập trung
axiosInstance.interceptors.response.use(
  response => response,
  (error: AxiosError) => {
    // Xử lý các trường hợp lỗi đặc biệt
    if (error.response) {
      // Server trả về lỗi (4xx, 5xx)
      const { status } = error.response;

      switch (status) {
        case 401:
          // Unauthorized - có thể redirect về login
          if (typeof window !== 'undefined') {
            localStorage.removeItem('accessToken');
            // window.location.href = '/login';
          }
          break;
        case 403:
          // Forbidden
          console.error('Bạn không có quyền truy cập');
          break;
        case 500:
          // Server error
          console.error('Lỗi server');
          break;
      }
    } else if (error.request) {
      // Request được gửi nhưng không nhận được response
      console.error('Không thể kết nối đến server');
    }

    return Promise.reject(error);
  },
);

// Custom instance cho Orval
export const customInstance = <T>(
  config: AxiosRequestConfig,
  options?: AxiosRequestConfig,
): Promise<AxiosResponse<T>> => {
  return axiosInstance({
    ...config,
    ...options,
  });
};

export default customInstance;

// Export type cho Orval
export type ErrorType<Error> = AxiosError<Error>;
