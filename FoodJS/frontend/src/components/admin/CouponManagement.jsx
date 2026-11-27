import styles from "../../pages/Dashboard.module.css";
import Button from "../ui/Button";
import { useState } from "react";
import Pagination from "../Pagination";
import usePagination from "../../hooks/usePagination";

const EMPTY_COUPON = {
  code: "",
  discount: "",
  discountType: "percentage",
  minOrderAmount: "",
  maxUses: "",
};

export default function CouponManagement({ coupons, onAddCoupon, onUpdateCoupon, onDeleteCoupon }) {
  const [form, setForm] = useState(EMPTY_COUPON);
  const [editId, setEditId] = useState(null);
  const pager = usePagination(coupons);

  const reset = () => {
    setForm(EMPTY_COUPON);
    setEditId(null);
  };

  const handleSubmit = (e) => {
    e.preventDefault();
    if (!form.code || !form.discount) return;

    const payload = {
      code: form.code.toUpperCase(),
      discount: parseFloat(form.discount),
      discountType: form.discountType,
      minOrderAmount: parseFloat(form.minOrderAmount) || 0,
      maxUses: form.maxUses === "" ? -1 : parseInt(form.maxUses),
      description: `${form.discountType === "percentage" ? form.discount + "%" : "₱" + form.discount} off`,
    };

    if (editId) {
      onUpdateCoupon(editId, payload);
    } else {
      onAddCoupon(payload);
    }
    reset();
  };

  const handleEdit = (coupon) => {
    setEditId(coupon.id);
    setForm({
      code: coupon.code,
      discount: coupon.discount,
      discountType: coupon.discountType,
      minOrderAmount: coupon.minOrderAmount,
      maxUses: coupon.maxUses,
    });
  };

  return (
    <section className={styles["admin-panel"]}>
      <div className={styles["admin-panel-header"]}>
        <h2>Coupon Management</h2>
      </div>

      <form className={styles["admin-form-grid"]} onSubmit={handleSubmit}>
        <input
          type="text"
          placeholder="Code (e.g., SAVE10)"
          aria-label="Coupon code"
          value={form.code}
          onChange={(e) => setForm({ ...form, code: e.target.value })}
          maxLength="20"
        />

        <input
          type="number"
          placeholder="Discount amount"
          aria-label="Discount amount"
          value={form.discount}
          onChange={(e) => setForm({ ...form, discount: e.target.value })}
          min="0"
        />

        <select
          aria-label="Discount type"
          value={form.discountType}
          onChange={(e) => setForm({ ...form, discountType: e.target.value })}
        >
          <option value="percentage">Percentage (%)</option>
          <option value="fixed">Fixed (₱)</option>
        </select>

        <input
          type="number"
          placeholder="Min order amount"
          aria-label="Minimum order amount"
          value={form.minOrderAmount}
          onChange={(e) => setForm({ ...form, minOrderAmount: e.target.value })}
          min="0"
        />

        <input
          type="number"
          placeholder="Max uses (-1 for unlimited)"
          aria-label="Maximum uses, -1 for unlimited"
          value={form.maxUses}
          onChange={(e) => setForm({ ...form, maxUses: e.target.value })}
          min="-1"
        />

        <div className={styles["admin-form-actions"]}>
          <Button variant="primary" type="submit" >
            {editId ? "Update Coupon" : "Add Coupon"}
          </Button>
          {editId && (
            <Button variant="light" type="button" onClick={reset}>
              Cancel
            </Button>
          )}
        </div>
      </form>

      <ul className={styles["admin-category-list"]}>
        {pager.pageItems.map((coupon) => (
          <li key={coupon.id}>
            <div>
              <h3>{coupon.code}</h3>
              <p>{coupon.description}</p>
              <span>Min: ₱{coupon.minOrderAmount} | Uses: {coupon.maxUses === -1 ? "∞" : coupon.usedCount + "/" + coupon.maxUses}</span>
            </div>
            <div className={styles["admin-item-actions"]}>
              <Button variant="light"
                type="button"

                onClick={() => handleEdit(coupon)}
              >
                Edit
              </Button>
              <Button variant="danger"
                type="button"

                onClick={() => onDeleteCoupon(coupon.id)}
              >
                Delete
              </Button>
            </div>
          </li>
        ))}
      </ul>
      <Pagination {...pager} label="Coupons" />
    </section>
  );
}
