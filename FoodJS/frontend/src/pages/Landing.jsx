import { Link, useNavigate } from "react-router-dom";
import logo from "../../assets/logo.png";

function handleMenuClick(e, navigate) {
  e.preventDefault();
  navigate("/login");
}

const categories = [
  {
    name: "Burger",
    image:
      "https://images.unsplash.com/photo-1568901346375-23c9450c58cd?auto=format&fit=crop&w=900&q=80",
    description: "Classic stacked burgers with house sauces and fresh buns.",
  },
  {
    name: "Chicken",
    image:
      "https://images.unsplash.com/photo-1626645738196-c2a7c87a8f58?auto=format&fit=crop&w=900&q=80",
    description: "Crispy fried chicken meals served hot and golden.",
  },
  {
    name: "Desserts",
    image:
      "https://images.unsplash.com/photo-1551024601-bec78aea704b?auto=format&fit=crop&w=900&q=80",
    description: "Sweet treats, sundaes, and pastry favorites.",
  },
  {
    name: "Drinks",
    image:
      "https://images.unsplash.com/photo-1461023058943-07fcbe16d735?auto=format&fit=crop&w=900&q=80",
    description: "Refreshing coolers, juices, and signature blends.",
  },
  {
    name: "Family Meals",
    image:
      "https://images.unsplash.com/photo-1606787366850-de6330128bfc?auto=format&fit=crop&w=900&q=80",
    description: "Sharing bundles built for celebrations and gatherings.",
  },
];

const featuredMenu = [
  {
    name: "Smoky Angus Burger",
    price: "₱169.00",
    image:
      "https://images.unsplash.com/photo-1550547660-d9450f859349?auto=format&fit=crop&w=900&q=80",
  },
  {
    name: "Crispy Chicken Box",
    price: "₱199.00",
    image:
      "https://images.unsplash.com/photo-1610057099443-fde8c4d50f91?auto=format&fit=crop&w=900&q=80",
  },
  {
    name: "Choco Caramel Sundae",
    price: "₱89.00",
    image:
      "https://images.unsplash.com/photo-1563805042-7684c019e1cb?auto=format&fit=crop&w=900&q=80",
  },
];

const bestSellers = [
  {
    name: "Cheese Burst Burger",
    tag: "Top Pick",
    image:
      "https://images.unsplash.com/photo-1586190848861-99aa4a171e90?auto=format&fit=crop&w=900&q=80",
  },
  {
    name: "Golden Chicken Meal",
    tag: "Most Ordered",
    image:
      "https://images.unsplash.com/photo-1527477396000-e27163b481c2?auto=format&fit=crop&w=900&q=80",
  },
  {
    name: "Family Party Bundle",
    tag: "Good for 4-6",
    image:
      "https://images.unsplash.com/photo-1516684669134-de6f7c473a2a?auto=format&fit=crop&w=900&q=80",
  },
];

const howItWorks = [
  {
    step: 1,
    title: "Browse Menu",
    description: "Explore thousands of items across all categories.",
    icon: "🔍",
  },
  {
    step: 2,
    title: "Add to Cart",
    description: "Pick your favorites and customize as needed.",
    icon: "🛒",
  },
  {
    step: 3,
    title: "Checkout",
    description: "Fast, secure payment with multiple options.",
    icon: "💳",
  },
  {
    step: 4,
    title: "Track & Enjoy",
    description: "Real-time updates until it arrives at your door.",
    icon: "📍",
  },
];

export default function Landing() {
  const navigate = useNavigate();

  return (
    <div className="landing-page">
      <header className="landing-header">
        <div className="landing-brand">
          <img src={logo} alt="FoodJS" />
          <div>
            <h1>FoodJS</h1>
            <p>Fresh picks for every craving.</p>
          </div>
        </div>

        <div className="landing-auth-actions">
          <Link className="header-link" to="/login">
            Login
          </Link>
          <Link className="header-link outline" to="/register">
            Sign Up
          </Link>
          <Link className="header-order-btn" to="/login">
            Order Now
          </Link>
        </div>
      </header>

      <main>
        <section className="landing-hero">
          <div className="hero-copy">
            <span className="hero-kicker">Featured This Week</span>
            <h2>
              Delicious comfort food,
              <br />
              delivered with style.
            </h2>
            <p>
              Discover handcrafted burgers, crisp chicken, sweet desserts, and meal bundles that
              bring everyone to the table.
            </p>
            <div className="hero-cta-row">
              <Link className="hero-btn primary" to="/login">
                Start Ordering
              </Link>
              <a className="hero-btn secondary" href="#featured-menu">
                View Featured Menu
              </a>
            </div>
          </div>

          <div className="hero-visual">
            <img
              src="https://images.unsplash.com/photo-1551782450-a2132b4ba21d?auto=format&fit=crop&w=1200&q=80"
              alt="Featured burger meal"
            />
          </div>
        </section>

        <section className="landing-section" id="featured-menu">
          <div className="section-title-row">
            <h3>Featured Menu</h3>
            <p>Chef-curated favorites made fresh all day.</p>
          </div>
          <div className="feature-grid">
            {featuredMenu.map((item) => (
              <article
                key={item.name}
                className="feature-card"
                onClick={(e) => handleMenuClick(e, navigate)}
                style={{ cursor: "pointer" }}
                role="button"
                tabIndex="0"
                onKeyDown={(e) => e.key === "Enter" && handleMenuClick(e, navigate)}
              >
                <img src={item.image} alt={item.name} />
                <div>
                  <h4>{item.name}</h4>
                  <p>{item.price}</p>
                </div>
              </article>
            ))}
          </div>
        </section>

        <section className="landing-section">
          <div className="section-title-row">
            <h3>Categories</h3>
            <p>Pick a category and find your perfect meal.</p>
          </div>
          <div className="category-grid">
            {categories.map((category) => (
              <article
                key={category.name}
                className="category-card"
                onClick={(e) => handleMenuClick(e, navigate)}
                style={{ cursor: "pointer" }}
                role="button"
                tabIndex="0"
                onKeyDown={(e) => e.key === "Enter" && handleMenuClick(e, navigate)}
              >
                <img src={category.image} alt={category.name} />
                <div>
                  <h4>{category.name}</h4>
                  <p>{category.description}</p>
                </div>
              </article>
            ))}
          </div>
        </section>

        <section className="landing-section">
          <div className="section-title-row">
            <h3>Best Sellers</h3>
            <p>Most-loved dishes chosen by our customers.</p>
          </div>
          <div className="best-seller-grid">
            {bestSellers.map((item) => (
              <article
                key={item.name}
                className="best-seller-card"
                onClick={(e) => handleMenuClick(e, navigate)}
                style={{ cursor: "pointer" }}
                role="button"
                tabIndex="0"
                onKeyDown={(e) => e.key === "Enter" && handleMenuClick(e, navigate)}
              >
                <img src={item.image} alt={item.name} />
                <div className="best-seller-content">
                  <span>{item.tag}</span>
                  <h4>{item.name}</h4>
                </div>
              </article>
            ))}
          </div>
        </section>

        <section className="landing-section landing-how-it-works">
          <div className="section-title-row">
            <h3>How It Works</h3>
            <p>Four simple steps to delicious food at your door.</p>
          </div>
          <div className="how-it-works-grid">
            {howItWorks.map((item) => (
              <div key={item.step} className="how-it-works-card">
                <div className="how-it-works-icon">{item.icon}</div>
                <h4>{item.title}</h4>
                <p>{item.description}</p>
              </div>
            ))}
          </div>
        </section>

        <section className="landing-final-cta">
          <h3>Ready to order?</h3>
          <p>Join thousands of satisfied customers. Order now and get 10% off your first order!</p>
          <Link className="landing-cta-btn" to="/login">Start Ordering Now</Link>
        </section>
      </main>
    </div>
  );
}
