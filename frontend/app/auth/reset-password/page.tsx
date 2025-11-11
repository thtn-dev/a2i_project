'use client';

import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import * as z from 'zod';
import { useRouter, useSearchParams } from 'next/navigation';
import Link from 'next/link';
import { usePostApiV1AccountResetPassword } from '@/lib/api/generated/account/account';
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
import { useState, useEffect } from 'react';

const resetPasswordSchema = z
  .object({
    email: z.string().email('Email không hợp lệ'),
    token: z.string().min(1, 'Token không hợp lệ'),
    newPassword: z
      .string()
      .min(6, 'Password phải có ít nhất 6 ký tự')
      .regex(
        /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)/,
        'Password phải chứa ít nhất 1 chữ hoa, 1 chữ thường và 1 số',
      ),
    confirmNewPassword: z.string().min(1, 'Vui lòng xác nhận password'),
  })
  .refine(data => data.newPassword === data.confirmNewPassword, {
    message: 'Password không khớp',
    path: ['confirmNewPassword'],
  });

type ResetPasswordFormData = z.infer<typeof resetPasswordSchema>;

export default function ResetPasswordPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const [errorMessage, setErrorMessage] = useState<string>('');
  const [successMessage, setSuccessMessage] = useState<string>('');

  const {
    register,
    handleSubmit,
    setValue,
    formState: { errors },
  } = useForm<ResetPasswordFormData>({
    resolver: zodResolver(resetPasswordSchema),
  });

  const resetPasswordMutation = usePostApiV1AccountResetPassword();

  useEffect(() => {
    // Get email and token from URL query parameters
    const email = searchParams.get('email');
    const token = searchParams.get('token');

    if (email) setValue('email', email);
    if (token) setValue('token', token);

    if (!email || !token) {
      setErrorMessage('Link không hợp lệ hoặc đã hết hạn. Vui lòng yêu cầu link mới.');
    }
  }, [searchParams, setValue]);

  const onSubmit = async (data: ResetPasswordFormData) => {
    try {
      setErrorMessage('');
      setSuccessMessage('');

      await resetPasswordMutation.mutateAsync({ data });

      setSuccessMessage('Đặt lại mật khẩu thành công! Đang chuyển đến trang đăng nhập...');

      // Redirect to login after 2 seconds
      setTimeout(() => {
        router.push('/auth/login');
      }, 2000);
    } catch (error: any) {
      console.error('Reset password error:', error);
      setErrorMessage(
        error?.message || 'Không thể đặt lại mật khẩu. Link có thể đã hết hạn hoặc không hợp lệ.',
      );
    }
  };

  return (
    <div className="flex min-h-screen items-center justify-center bg-gray-50 px-4 py-12 dark:bg-gray-900">
      <Card className="w-full max-w-md">
        <CardHeader className="space-y-1">
          <CardTitle className="text-center">Đặt lại mật khẩu</CardTitle>
          <CardDescription className="text-center">
            Nhập mật khẩu mới cho tài khoản của bạn
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
                placeholder="Email của bạn"
                {...register('email')}
                error={errors.email?.message}
                disabled
              />
            </div>

            <input type="hidden" {...register('token')} />

            <div className="space-y-2">
              <Label htmlFor="newPassword" required>
                Mật khẩu mới
              </Label>
              <Input
                id="newPassword"
                type="password"
                placeholder="Nhập mật khẩu mới"
                {...register('newPassword')}
                error={errors.newPassword?.message}
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="confirmNewPassword" required>
                Xác nhận mật khẩu mới
              </Label>
              <Input
                id="confirmNewPassword"
                type="password"
                placeholder="Nhập lại mật khẩu mới"
                {...register('confirmNewPassword')}
                error={errors.confirmNewPassword?.message}
              />
            </div>

            <Button type="submit" className="w-full" isLoading={resetPasswordMutation.isPending}>
              Đặt lại mật khẩu
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
            Chưa nhận được email?{' '}
            <Link
              href="/auth/forgot-password"
              className="text-blue-600 hover:underline dark:text-blue-400"
            >
              Gửi lại
            </Link>
          </p>
        </CardFooter>
      </Card>
    </div>
  );
}
