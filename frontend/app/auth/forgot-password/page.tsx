'use client';

import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import * as z from 'zod';
import Link from 'next/link';
import { usePostApiV1AccountForgotPassword } from '@/lib/api/generated/account/account';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { useState } from 'react';

const forgotPasswordSchema = z.object({
  email: z.string().email('Email không hợp lệ'),
});

type ForgotPasswordFormData = z.infer<typeof forgotPasswordSchema>;

export default function ForgotPasswordPage() {
  const [errorMessage, setErrorMessage] = useState<string>('');
  const [successMessage, setSuccessMessage] = useState<string>('');

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ForgotPasswordFormData>({
    resolver: zodResolver(forgotPasswordSchema),
  });

  const forgotPasswordMutation = usePostApiV1AccountForgotPassword();

  const onSubmit = async (data: ForgotPasswordFormData) => {
    try {
      setErrorMessage('');
      setSuccessMessage('');

      await forgotPasswordMutation.mutateAsync({ data });

      setSuccessMessage('Email khôi phục mật khẩu đã được gửi! Vui lòng kiểm tra hộp thư của bạn.');
    } catch (error: any) {
      console.error('Forgot password error:', error);
      setErrorMessage(
        error?.message || 'Không thể gửi email khôi phục. Vui lòng kiểm tra lại địa chỉ email.',
      );
    }
  };

  return (
    <div className="flex min-h-screen items-center justify-center bg-gray-50 px-4 py-12 dark:bg-gray-900">
      <Card className="w-full max-w-md">
        <CardHeader className="space-y-1">
          <CardTitle className="text-center">Quên mật khẩu</CardTitle>
          <CardDescription className="text-center">
            Nhập email của bạn để nhận link khôi phục mật khẩu
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
            {errorMessage && (
              <div className="rounded-md bg-red-50 p-3 dark:bg-red-900/20">
                <p className="text-sm text-red-800 dark:text-red-400">{errorMessage}</p>
              </div>
            )}

            {successMessage && (
              <div className="rounded-md bg-green-50 p-3 dark:bg-green-900/20">
                <p className="text-sm text-green-800 dark:text-green-400">{successMessage}</p>
              </div>
            )}

            <div className="space-y-2">
              <Label htmlFor="email" required>
                Email
              </Label>
              <Input
                id="email"
                type="email"
                placeholder="Nhập email của bạn"
                {...register('email')}
                error={errors.email?.message}
              />
            </div>

            <Button type="submit" className="w-full" isLoading={forgotPasswordMutation.isPending}>
              Gửi email khôi phục
            </Button>

            <div className="text-center">
              <Link
                href="/auth/login"
                className="text-sm text-blue-600 hover:underline dark:text-blue-400"
              >
                Quay lại đăng nhập
              </Link>
            </div>
          </form>
        </CardContent>
        <CardFooter className="flex justify-center">
          <p className="text-sm text-gray-600 dark:text-gray-400">
            Chưa có tài khoản?{' '}
            <Link
              href="/auth/register"
              className="text-blue-600 hover:underline dark:text-blue-400"
            >
              Đăng ký ngay
            </Link>
          </p>
        </CardFooter>
      </Card>
    </div>
  );
}
