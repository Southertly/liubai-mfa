export const SUPPORTED_TOTP_ALGORITHMS = ["SHA1", "SHA256", "SHA512"] as const;

export type TotpAlgorithm = (typeof SUPPORTED_TOTP_ALGORITHMS)[number];

export interface PortableAccountData {
  id?: string;
  issuer: string;
  account: string;
  secret: string;
  algorithm: TotpAlgorithm;
  digits: number;
  period: number;
  createdAt: number;
  group?: string;
  color?: string;
}

const BASE32_PATTERN = /^[A-Z2-7]+$/;
const MAX_ACCOUNTS = 500;
const MAX_TEXT_LENGTH = 256;

export function normalizeTotpAlgorithm(value: unknown): TotpAlgorithm {
  const normalized = typeof value === "string" ? value.trim().toUpperCase() : "SHA1";
  if (!SUPPORTED_TOTP_ALGORITHMS.includes(normalized as TotpAlgorithm)) {
    throw new Error(`不支持的 TOTP 算法：${normalized || "空"}`);
  }
  return normalized as TotpAlgorithm;
}

export function normalizeBase32Secret(value: unknown): string {
  if (typeof value !== "string") throw new Error("账户密钥必须是文本");
  const normalized = value.toUpperCase().replace(/[\s=-]/g, "");
  if (normalized.length < 8 || normalized.length > 1024 || !BASE32_PATTERN.test(normalized)) {
    throw new Error("账户密钥不是有效的 Base32");
  }
  return normalized;
}

function requiredText(value: unknown, field: string): string {
  if (typeof value !== "string") throw new Error(`${field}必须是文本`);
  const normalized = value.trim();
  if (!normalized || normalized.length > MAX_TEXT_LENGTH) {
    throw new Error(`${field}不能为空且不能超过 ${MAX_TEXT_LENGTH} 个字符`);
  }
  return normalized;
}

function optionalText(value: unknown, field: string): string | undefined {
  if (value === undefined || value === null || value === "") return undefined;
  if (typeof value !== "string") throw new Error(`${field}必须是文本`);
  const normalized = value.trim();
  if (normalized.length > MAX_TEXT_LENGTH) throw new Error(`${field}过长`);
  return normalized || undefined;
}

function normalizeDigits(value: unknown): number {
  const digits = value === undefined ? 6 : value;
  if (!Number.isInteger(digits) || (digits !== 6 && digits !== 7 && digits !== 8)) {
    throw new Error("TOTP 位数必须是 6、7 或 8");
  }
  return digits as number;
}

function normalizePeriod(value: unknown): number {
  const period = value === undefined ? 30 : value;
  if (!Number.isInteger(period) || (period as number) < 1 || (period as number) > 300) {
    throw new Error("TOTP 周期必须是 1 到 300 秒之间的整数");
  }
  return period as number;
}

function normalizeCreatedAt(value: unknown): number {
  if (value === undefined) return Date.now();
  if (typeof value !== "number" || !Number.isFinite(value) || value < 0) {
    throw new Error("账户创建时间无效");
  }
  return value;
}

export function normalizePortableAccount(value: unknown): PortableAccountData {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    throw new Error("账户记录格式无效");
  }
  const account = value as Record<string, unknown>;
  const id = optionalText(account.id, "账户 ID");
  return {
    ...(id ? { id } : {}),
    issuer: optionalText(account.issuer, "服务名称") ?? "",
    account: requiredText(account.account, "账户名称"),
    secret: normalizeBase32Secret(account.secret),
    algorithm: normalizeTotpAlgorithm(account.algorithm),
    digits: normalizeDigits(account.digits),
    period: normalizePeriod(account.period),
    createdAt: normalizeCreatedAt(account.createdAt),
    group: optionalText(account.group, "分组名称"),
    color: optionalText(account.color, "标签颜色"),
  };
}

export function normalizePortableAccounts(value: unknown): PortableAccountData[] {
  if (!Array.isArray(value) || value.length > MAX_ACCOUNTS) {
    throw new Error("账户列表格式无效或数量过多");
  }
  return value.map(normalizePortableAccount);
}
