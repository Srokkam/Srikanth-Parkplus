const rupees = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' });

/** 199900 -> "₹1,999.00". The one place amounts are formatted. */
export function formatPaise(paise: number): string {
  return rupees.format(paise / 100);
}

/** 1999.5 -> 199950. What a person types in rupees becomes the integer paise the API stores. */
export function rupeesToPaise(rupeesAmount: number): number {
  return Math.round(rupeesAmount * 100);
}
