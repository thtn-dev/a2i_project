"use client";

import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import * as z from "zod";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { usePostApiV1AuthRegister } from "@/lib/api/generated/auth/auth";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { useState } from "react";

const registerSchema = z
  .object({
    username: z
      .string()
      .min(3, "Username phải có ít nhất 3 ký tự")
      .max(50, "Username không được quá 50 ký tự"),
    email: z.string().email("Email không hợp lệ"),
    password: z
      .string()
      .min(6, "Password phải có ít nhất 6 ký tự")
      .regex(
        /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)/,
        "Password phải chứa ít nhất 1 chữ hoa, 1 chữ thường và 1 số"
      ),
    confirmPassword: z.string().min(1, "Vui lòng xác nhận password"),
  })
  .refine((data) => data.password === data.confirmPassword, {
    message: "Password không khớp",
    path: ["confirmPassword"],
  });

type RegisterFormData = z.infer<typeof registerSchema>;

export default function RegisterPage() {
  const router = useRouter();
  const [errorMessage, setErrorMessage] = useState<string>("");
  const [successMessage, setSuccessMessage] = useState<string>("");

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<RegisterFormData>({
    resolver: zodResolver(registerSchema),
  });

  const registerMutation = usePostApiV1AuthRegister();

  const onSubmit = async (data: RegisterFormData) => {
    try {
      setErrorMessage("");
      setSuccessMessage("");

      await registerMutation.mutateAsync({ data });

      setSuccessMessage(
        "Đăng ký thành công! Vui lòng kiểm tra email để xác nhận tài khoản."
      );

      // Redirect to login after 2 seconds
      setTimeout(() => {
        router.push("/auth/login");
      }, 2000);
    } catch (error: any) {
      console.error("Register error:", error);
      setErrorMessage(
        error?.message ||
          "Đăng ký thất bại. Username hoặc email có thể đã tồn tại."
      );
    }
  };

  return (
    <div className="flex min-h-screen items-center justify-center bg-gray-50 px-4 py-12 dark:bg-gray-900">
      <Card className="w-full max-w-md">
        <CardHeader className="space-y-1">
          <CardTitle className="text-center">Đăng ký tài khoản</CardTitle>
          <CardDescription className="text-center">
            Tạo tài khoản mới để bắt đầu
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
            {errorMessage && (
              <div className="rounded-md bg-red-50 p-3 dark:bg-red-900/20">
                <p className="text-sm text-red-800 dark:text-red-400">
                  {errorMessage}
                </p>
              </div>
            )}

            {successMessage && (
              <div className="rounded-md bg-green-50 p-3 dark:bg-green-900/20">
                <p className="text-sm text-green-800 dark:text-green-400">
                  {successMessage}
                </p>
              </div>
            )}

            <div className="space-y-2">
              <Label htmlFor="username" required>
                Username
              </Label>
              <Input
                id="username"
                type="text"
                placeholder="Nhập username"
                {...register("username")}
                error={errors.username?.message}
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="email" required>
                Email
              </Label>
              <Input
                id="email"
                type="email"
                placeholder="Nhập email"
                {...register("email")}
                error={errors.email?.message}
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="password" required>
                Password
              </Label>
              <Input
                id="password"
                type="password"
                placeholder="Nhập password"
                {...register("password")}
                error={errors.password?.message}
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="confirmPassword" required>
                Xác nhận Password
              </Label>
              <Input
                id="confirmPassword"
                type="password"
                placeholder="Nhập lại password"
                {...register("confirmPassword")}
                error={errors.confirmPassword?.message}
              />
            </div>

            <Button
              type="submit"
              className="w-full"
              isLoading={registerMutation.isPending}
            >
              Đăng ký
            </Button>
          </form>
        </CardContent>
        <CardFooter className="flex justify-center">
          <p className="text-sm text-gray-600 dark:text-gray-400">
            Đã có tài khoản?{" "}
            <Link
              href="/auth/login"
              className="text-blue-600 hover:underline dark:text-blue-400"
            >
              Đăng nhập ngay
            </Link>
          </p>
        </CardFooter>
      </Card>
    </div>
  );
}
