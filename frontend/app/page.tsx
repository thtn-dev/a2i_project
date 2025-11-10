"use client";

import Link from "next/link";
import { useAuth } from "@/lib/providers/auth-provider";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

export default function Home() {
  const { user, isAuthenticated, isLoading, logout } = useAuth();

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-gray-50 dark:bg-gray-900">
        <p className="text-gray-600 dark:text-gray-400">Đang tải...</p>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-gray-50 px-4 dark:bg-gray-900">
      <Card className="w-full max-w-md">
        <CardHeader>
          <CardTitle className="text-center">
            {isAuthenticated ? "Chào mừng trở lại!" : "Chào mừng"}
          </CardTitle>
          <CardDescription className="text-center">
            {isAuthenticated
              ? "Bạn đã đăng nhập thành công"
              : "Vui lòng đăng nhập để tiếp tục"}
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {isAuthenticated && user ? (
            <div className="space-y-4">
              <div className="rounded-lg bg-gray-50 p-4 dark:bg-gray-800">
                <h3 className="mb-2 text-sm font-semibold text-gray-700 dark:text-gray-300">
                  Thông tin tài khoản
                </h3>
                <div className="space-y-2 text-sm">
                  <div className="flex justify-between">
                    <span className="text-gray-600 dark:text-gray-400">
                      Username:
                    </span>
                    <span className="font-medium text-gray-900 dark:text-gray-100">
                      {user.username}
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-gray-600 dark:text-gray-400">
                      Email:
                    </span>
                    <span className="font-medium text-gray-900 dark:text-gray-100">
                      {user.email}
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-gray-600 dark:text-gray-400">
                      Email xác thực:
                    </span>
                    <span
                      className={`font-medium ${
                        user.emailConfirmed
                          ? "text-green-600 dark:text-green-400"
                          : "text-yellow-600 dark:text-yellow-400"
                      }`}
                    >
                      {user.emailConfirmed ? "Đã xác thực" : "Chưa xác thực"}
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-gray-600 dark:text-gray-400">
                      2FA:
                    </span>
                    <span
                      className={`font-medium ${
                        user.twoFactorEnabled
                          ? "text-green-600 dark:text-green-400"
                          : "text-gray-600 dark:text-gray-400"
                      }`}
                    >
                      {user.twoFactorEnabled ? "Đã bật" : "Chưa bật"}
                    </span>
                  </div>
                </div>
              </div>
              <Button onClick={logout} variant="outline" className="w-full">
                Đăng xuất
              </Button>
            </div>
          ) : (
            <div className="space-y-3">
              <Link href="/auth/login" className="block">
                <Button className="w-full">Đăng nhập</Button>
              </Link>
              <Link href="/auth/register" className="block">
                <Button variant="outline" className="w-full">
                  Đăng ký
                </Button>
              </Link>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
