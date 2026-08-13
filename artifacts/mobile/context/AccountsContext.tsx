import AsyncStorage from "@react-native-async-storage/async-storage";
import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from "react";

import { decryptString, encryptString, SecureEnvelope } from "@/utils/secureCrypto";
import { getOrCreateDeviceEncryptionKey } from "@/utils/secureStorage";
import { selectUniqueAccountsBySecret } from "@/utils/qrTransfer";

export interface Account {
  id: string;
  issuer: string;
  account: string;
  secret: string;
  group?: string;
  color?: string;
  createdAt: number;
}

interface AccountsContextValue {
  accounts: Account[];
  groups: string[];
  addAccount: (data: Omit<Account, "id" | "createdAt">) => Promise<void>;
  importAccounts: (data: Omit<Account, "id">[]) => Promise<{ imported: number; skipped: number }>;
  removeAccount: (id: string) => Promise<void>;
  updateAccount: (
    id: string,
    updates: Partial<Omit<Account, "id" | "createdAt">>
  ) => Promise<void>;
  renameGroup: (oldName: string, newName: string) => Promise<void>;
  removeGroup: (name: string) => Promise<void>;
  loading: boolean;
  storageError: string | null;
}

const AccountsContext = createContext<AccountsContextValue | null>(null);

const STORAGE_KEY = "mfa_accounts";

interface EncryptedAccountStore {
  version: 2;
  encrypted: true;
  data: SecureEnvelope;
}

function generateId(): string {
  return Date.now().toString() + Math.random().toString(36).substring(2, 9);
}

export function AccountsProvider({ children }: { children: React.ReactNode }) {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [loading, setLoading] = useState(true);
  const [storageError, setStorageError] = useState<string | null>(null);

  useEffect(() => {
    (async () => {
      try {
        const [raw, deviceKey] = await Promise.all([
          AsyncStorage.getItem(STORAGE_KEY),
          getOrCreateDeviceEncryptionKey(),
        ]);
        if (!raw) return;

        const stored = JSON.parse(raw) as Account[] | EncryptedAccountStore;
        if (Array.isArray(stored)) {
          // One-time migration from the legacy plaintext store.
          const migrated: EncryptedAccountStore = {
            version: 2,
            encrypted: true,
            data: encryptString(JSON.stringify(stored), deviceKey),
          };
          await AsyncStorage.setItem(STORAGE_KEY, JSON.stringify(migrated));
          setAccounts(stored);
          return;
        }

        if (stored.version !== 2 || stored.encrypted !== true) {
          throw new Error("Unsupported account storage format");
        }
        const decrypted = JSON.parse(decryptString(stored.data, deviceKey));
        if (!Array.isArray(decrypted)) throw new Error("Invalid account storage");
        setAccounts(decrypted as Account[]);
      } catch {
        setStorageError("无法解锁本地账户数据。为防止覆盖，账户修改已暂停。");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const saveAccounts = useCallback(async (next: Account[]) => {
    if (storageError) throw new Error(storageError);
    const deviceKey = await getOrCreateDeviceEncryptionKey();
    const encryptedStore: EncryptedAccountStore = {
      version: 2,
      encrypted: true,
      data: encryptString(JSON.stringify(next), deviceKey),
    };
    await AsyncStorage.setItem(STORAGE_KEY, JSON.stringify(encryptedStore));
    setAccounts(next);
  }, [storageError]);

  const addAccount = useCallback(
    async (data: Omit<Account, "id" | "createdAt">) => {
      const newAccount: Account = {
        ...data,
        id: generateId(),
        createdAt: Date.now(),
      };
      await saveAccounts([...accounts, newAccount]);
    },
    [accounts, saveAccounts]
  );

  const importAccounts = useCallback(
    async (data: Omit<Account, "id">[]) => {
      const selected = selectUniqueAccountsBySecret(accounts, data);
      const imported: Account[] = selected.accepted.map((candidate, index) => ({
        ...candidate,
        id: `${generateId()}-${index}`,
        createdAt: candidate.createdAt || Date.now(),
      }));
      if (imported.length > 0) await saveAccounts([...imported, ...accounts]);
      return { imported: imported.length, skipped: selected.skipped };
    },
    [accounts, saveAccounts]
  );

  const removeAccount = useCallback(
    async (id: string) => {
      await saveAccounts(accounts.filter((a) => a.id !== id));
    },
    [accounts, saveAccounts]
  );

  const updateAccount = useCallback(
    async (
      id: string,
      updates: Partial<Omit<Account, "id" | "createdAt">>
    ) => {
      await saveAccounts(
        accounts.map((a) => (a.id === id ? { ...a, ...updates } : a))
      );
    },
    [accounts, saveAccounts]
  );

  const renameGroup = useCallback(
    async (oldName: string, newName: string) => {
      await saveAccounts(
        accounts.map((a) =>
          a.group === oldName ? { ...a, group: newName.trim() } : a
        )
      );
    },
    [accounts, saveAccounts]
  );

  const removeGroup = useCallback(
    async (name: string) => {
      await saveAccounts(
        accounts.map((a) =>
          a.group === name ? { ...a, group: undefined } : a
        )
      );
    },
    [accounts, saveAccounts]
  );

  const groups = useMemo(
    () =>
      [
        ...new Set(
          accounts
            .map((a) => a.group)
            .filter((g): g is string => !!g)
        ),
      ].sort((a, b) => a.localeCompare(b)),
    [accounts]
  );

  return (
    <AccountsContext.Provider
      value={{ accounts, groups, addAccount, importAccounts, removeAccount, updateAccount, renameGroup, removeGroup, loading, storageError }}
    >
      {children}
    </AccountsContext.Provider>
  );
}

export function useAccounts(): AccountsContextValue {
  const ctx = useContext(AccountsContext);
  if (!ctx) throw new Error("useAccounts must be used within AccountsProvider");
  return ctx;
}
