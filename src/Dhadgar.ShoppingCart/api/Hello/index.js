module.exports = async function (context, req) {
  context.res = {
    status: 200,
    headers: {
      "Content-Type": "application/json"
    },
    body: {
      message: "Hello from Dhadgar.ShoppingCart Functions",
      route: "GET /api/Hello"
    }
  };
};
