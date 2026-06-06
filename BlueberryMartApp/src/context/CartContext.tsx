import React, { createContext, useContext, useMemo, useState } from 'react';

export interface Branch { id: string; name: string; city: string; }
export interface CartItem { itemId: string; itemName: string; price: number; quantity: number; }
export interface BranchCart { branch: Branch; items: CartItem[]; }

interface CartContextValue {
  carts: Record<string, BranchCart>;
  totalCount: number;
  branchCount: number;
  addToCart: (item: { id: string; itemName: string; price: number }, branch: Branch) => void;
  updateQty: (branchId: string, itemId: string, delta: number) => void;
  clearBranch: (branchId: string) => void;
}

const CartContext = createContext<CartContextValue | null>(null);

export function CartProvider({ children }: { children: React.ReactNode }) {
  const [carts, setCarts] = useState<Record<string, BranchCart>>({});

  function addToCart(item: { id: string; itemName: string; price: number }, branch: Branch) {
    setCarts(prev => {
      const existing = prev[branch.id]?.items ?? [];
      const found = existing.find(c => c.itemId === item.id);
      const updated = found
        ? existing.map(c => (c.itemId === item.id ? { ...c, quantity: c.quantity + 1 } : c))
        : [...existing, { itemId: item.id, itemName: item.itemName, price: item.price, quantity: 1 }];
      return { ...prev, [branch.id]: { branch, items: updated } };
    });
  }

  function updateQty(branchId: string, itemId: string, delta: number) {
    setCarts(prev => {
      const bc = prev[branchId];
      if (!bc) return prev;
      const updated = bc.items
        .map(c => (c.itemId === itemId ? { ...c, quantity: c.quantity + delta } : c))
        .filter(c => c.quantity > 0);
      if (updated.length === 0) { const { [branchId]: _removed, ...rest } = prev; return rest; }
      return { ...prev, [branchId]: { ...bc, items: updated } };
    });
  }

  function clearBranch(branchId: string) {
    setCarts(prev => { const { [branchId]: _removed, ...rest } = prev; return rest; });
  }

  const { totalCount, branchCount } = useMemo(() => {
    const values = Object.values(carts);
    return {
      totalCount: values.reduce((s, bc) => s + bc.items.reduce((a, i) => a + i.quantity, 0), 0),
      branchCount: values.length,
    };
  }, [carts]);

  return (
    <CartContext.Provider value={{ carts, totalCount, branchCount, addToCart, updateQty, clearBranch }}>
      {children}
    </CartContext.Provider>
  );
}

export function useCart() {
  const ctx = useContext(CartContext);
  if (!ctx) throw new Error('useCart must be used within a CartProvider');
  return ctx;
}
